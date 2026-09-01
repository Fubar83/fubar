using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Reporting;

/// <summary>What a report is written as.</summary>
public enum ReportFormat
{
    /// <summary>Plain text, for a build log.</summary>
    Text,

    /// <summary>One self-contained page, for a human to open or a CI job to publish as an artifact.</summary>
    Html,

    /// <summary>For something other than a person to read - a gate, a dashboard, a bot.</summary>
    Json,

    /// <summary>A unified diff, which git apply and patch understand.</summary>
    Patch,
}

/// <summary>
/// Writes a <see cref="ComparisonReport"/> out.
///
/// Four formats because they answer four different questions, and a tool that only had one would be
/// wrong for three of them: a build log wants a line of text, a pull request wants a page someone can
/// look at, a gate wants fields it can test, and a patch wants to be applied. All four are produced
/// from the same report, so they cannot disagree about what was found.
/// </summary>
public static class ReportRenderer
{
    /// <summary>
    /// The format a file name implies, or null when it implies none - so <c>--report out.html</c>
    /// needs no second flag, and an unknown extension is the caller's problem to report rather than
    /// something to guess at.
    /// </summary>
    public static ReportFormat? FormatFor(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => ReportFormat.Html,
            ".json" => ReportFormat.Json,
            ".patch" or ".diff" => ReportFormat.Patch,
            ".txt" or ".log" or ".md" => ReportFormat.Text,
            _ => null,
        };

    /// <summary>
    /// Renders the report. <paramref name="patch"/> is supplied by the caller rather than computed
    /// here: a unified diff comes from <c>UnifiedPatch</c> over the full result, which a report - a
    /// summary with context around each hunk - deliberately no longer holds.
    /// </summary>
    public static string Render(ComparisonReport report, ReportFormat format, string? patch = null) => format switch
    {
        ReportFormat.Html => Html(report),
        ReportFormat.Json => Json(report),
        ReportFormat.Patch => patch ?? string.Empty,
        _ => Text(report),
    };

    private static string Text(ComparisonReport report)
    {
        var text = new StringBuilder();

        text.Append(report.LeftPath).Append(" <-> ").AppendLine(report.RightPath);
        text.AppendLine(report.Summary());

        foreach (var hunk in report.Hunks)
        {
            text.AppendLine();
            text.Append("--- change ").Append(hunk.Number).AppendLine(" ---");

            foreach (var row in hunk.Rows)
            {
                // The prefix column is diff's own vocabulary, which anyone reading a build log already
                // knows: a modified line is a removal and an addition, one under the other.
                switch (row.Kind)
                {
                    case ChangeKind.Modified:
                        text.Append("- ").AppendLine(row.LeftText);
                        text.Append("+ ").AppendLine(row.RightText);
                        break;

                    case ChangeKind.Deleted:
                        text.Append("- ").AppendLine(row.LeftText);
                        break;

                    case ChangeKind.Inserted:
                        text.Append("+ ").AppendLine(row.RightText);
                        break;

                    default:
                        text.Append("  ").AppendLine(row.LeftText ?? row.RightText);
                        break;
                }
            }
        }

        return text.ToString();
    }

    private static string Json(ComparisonReport report)
    {
        var buffer = new System.IO.MemoryStream();

        // Indented, because a report is read by people at least as often as by programs, and a diff
        // of two reports is worth something too. Relaxed escaping for the same reason: it writes a
        // quote as \" rather than as a 0022 escape, and a document full of those is not one anybody
        // wants to scan. "Unsafe" there means unsafe to drop into HTML unescaped, which a .json
        // report never is.
        var writerOptions = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using (var writer = new Utf8JsonWriter(buffer, writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("left", report.LeftPath);
            writer.WriteString("right", report.RightPath);
            writer.WriteBoolean("identical", report.AreIdentical);
            writer.WriteString("summary", report.Summary());

            writer.WriteStartObject("counts");
            writer.WriteNumber("changes", report.Hunks.Count);
            writer.WriteNumber("added", report.Added);
            writer.WriteNumber("removed", report.Removed);
            writer.WriteNumber("changed", report.Changed);
            writer.WriteNumber("moved", report.Moved);

            if (report.SemanticChanges is { } semantic)
            {
                writer.WriteNumber("structural", semantic);
            }

            writer.WriteEndObject();

            // The member-level answer, for a pipeline that wants to ask "did anything MEANINGFUL
            // change" rather than "how many lines differ". Written only when the structural pass
            // actually ran, so an absent object means "not source code we can read" rather than
            // "nothing changed" - the same distinction the null on SemanticChanges draws.
            if (report.CodeStructure.Any)
            {
                writer.WriteStartObject("code");
                writer.WriteString("summary", report.CodeStructure.Caption());
                writer.WriteBoolean("noFunctionalChange", report.CodeStructure.NoFunctionalChange);
                writer.WriteNumber("added", report.CodeStructure.Added);
                writer.WriteNumber("removed", report.CodeStructure.Removed);
                writer.WriteNumber("changed", report.CodeStructure.Modified);
                writer.WriteNumber("renamed", report.CodeStructure.Renamed);
                writer.WriteNumber("reformatted", report.CodeStructure.Cosmetic);
                writer.WriteNumber("moved", report.CodeStructure.Moved);
                writer.WriteEndObject();
            }

            if (report.FormatDifference is { } format)
            {
                writer.WriteString("formatDifference", format);
            }

            writer.WriteStartArray("changes");
            foreach (var hunk in report.Hunks)
            {
                writer.WriteStartObject();
                writer.WriteNumber("number", hunk.Number);
                writer.WriteStartArray("rows");

                foreach (var row in hunk.Rows)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", row.Kind.ToString().ToLowerInvariant());

                    if (row.IsMoved)
                    {
                        writer.WriteBoolean("moved", true);
                    }

                    WriteSide(writer, "left", row.LeftNumber, row.LeftText);
                    WriteSide(writer, "right", row.RightNumber, row.RightText);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteSide(Utf8JsonWriter writer, string name, int? number, string? text)
    {
        // A side with no line here is omitted entirely rather than written as null: "this row has no
        // left" is what an insertion IS, and a consumer testing for the key gets a straight answer.
        if (number is not { } line)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("line", line);
        writer.WriteString("text", text ?? string.Empty);
        writer.WriteEndObject();
    }

    /// <summary>
    /// One self-contained page - no stylesheet to lose, no script at all - so it survives being
    /// attached to a build, mailed, or opened from a network share years later.
    /// </summary>
    private static string Html(ComparisonReport report)
    {
        var html = new StringBuilder();

        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.Append("<title>")
            .Append(Escape(report.LeftPath))
            .Append(" ↔ ")
            .Append(Escape(report.RightPath))
            .AppendLine("</title>");

        html.AppendLine("""
            <style>
              :root { color-scheme: light dark; }
              body { font: 14px/1.5 system-ui, -apple-system, Segoe UI, sans-serif; margin: 2rem; }
              h1 { font-size: 1.1rem; font-family: ui-monospace, Cascadia Mono, Consolas, monospace; }
              .summary { color: #666; margin-bottom: 1.5rem; }
              .format { color: #a15c00; }
              table { border-collapse: collapse; width: 100%; margin-bottom: 1.5rem;
                      font: 12px/1.45 ui-monospace, Cascadia Mono, Consolas, monospace; }
              caption { text-align: left; color: #666; padding: .4rem 0; font-family: system-ui, sans-serif; }
              td { padding: 0 .5rem; vertical-align: top; white-space: pre-wrap; word-break: break-word; }
              td.num { width: 1%; text-align: right; color: #999; user-select: none; }
              tr.del td { background: #ffecec; }
              tr.ins td { background: #eaf2ff; }
              tr.mod td { background: #fff6e5; }
              tr.moved td { background: #eef; font-style: italic; }
              @media (prefers-color-scheme: dark) {
                body { background: #16181d; color: #d7dae0; }
                .summary, caption, td.num { color: #8b909a; }
                tr.del td { background: #3a2226; }
                tr.ins td { background: #1e2a44; }
                tr.mod td { background: #3a3222; }
                tr.moved td { background: #232a3a; }
              }
            </style></head><body>
            """);

        html.Append("<h1>")
            .Append(Escape(report.LeftPath))
            .Append(" ↔ ")
            .Append(Escape(report.RightPath))
            .AppendLine("</h1>");

        html.Append("<p class=\"summary\">").Append(Escape(report.Summary())).AppendLine("</p>");

        if (report.FormatDifference is { } difference)
        {
            html.Append("<p class=\"format\">Format: ").Append(Escape(difference)).AppendLine("</p>");
        }

        foreach (var hunk in report.Hunks)
        {
            html.Append("<table><caption>Change ").Append(hunk.Number).AppendLine("</caption><tbody>");

            foreach (var row in hunk.Rows)
            {
                switch (row.Kind)
                {
                    case ChangeKind.Modified:
                        Row(html, "mod", row.IsMoved, row.LeftNumber, row.LeftText, "-");
                        Row(html, "mod", row.IsMoved, row.RightNumber, row.RightText, "+");
                        break;

                    case ChangeKind.Deleted:
                        Row(html, "del", row.IsMoved, row.LeftNumber, row.LeftText, "-");
                        break;

                    case ChangeKind.Inserted:
                        Row(html, "ins", row.IsMoved, row.RightNumber, row.RightText, "+");
                        break;

                    default:
                        Row(html, "ctx", false, row.LeftNumber ?? row.RightNumber, row.LeftText ?? row.RightText, " ");
                        break;
                }
            }

            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("</body></html>");

        return html.ToString();
    }

    private static void Row(StringBuilder html, string cssClass, bool moved, int? number, string? text, string marker)
    {
        html.Append("<tr class=\"").Append(cssClass);

        if (moved)
        {
            html.Append(" moved");
        }

        html.Append("\"><td class=\"num\">")
            .Append(number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            .Append("</td><td class=\"num\">")
            .Append(Escape(marker))
            .Append("</td><td>")
            .Append(Escape(text ?? string.Empty))
            .AppendLine("</td></tr>");
    }

    private static string Escape(string text) => HtmlEncoder.Default.Encode(text);
}
