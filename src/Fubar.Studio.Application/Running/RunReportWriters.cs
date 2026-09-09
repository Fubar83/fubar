using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Running;

/// <summary>The whole report as JSON, for something the caller wrote themselves.</summary>
public static class JsonRunReport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // The default encoder escapes anything non-ASCII, which would turn every accented request name
        // and every non-Latin assertion message into \uXXXX in a file a person is meant to read.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(RunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return JsonSerializer.Serialize(
            new
            {
                ok = report.Ok,
                summary = report.Summary(),
                total = report.Total,
                passed = report.Passed,
                failed = report.Failed,
                errored = report.Errored,
                skipped = report.Skipped,
                differing = report.Differing,
                uncomparable = report.Uncomparable,
                tolerated = report.Tolerated,
                assertionsPassed = report.AssertionsPassed,
                assertionsFailed = report.AssertionsFailed,
                elapsedMs = report.ElapsedMilliseconds,
                cancelled = report.WasCancelled,
                stoppedEarly = report.StoppedEarly,
                steps = report.Steps.Select(s => new
                {
                    order = s.Step.Order,
                    name = s.Step.QualifiedName,
                    path = s.Step.FilePath,
                    status = s.Status.ToString().ToLowerInvariant(),
                    statusCode = s.StatusCode,
                    elapsedMs = s.ElapsedMilliseconds,
                    sizeBytes = s.SizeBytes,
                    // Reported alongside the status rather than folded into it, exactly as in the UI.
                    unexpectedStatus = s.IsUnexpectedStatus && s.Assertions.Count == 0,
                    // Cleanup is in the list like anything else and carries no verdict, so a reader
                    // counting statuses has to be able to tell it apart.
                    teardown = s.Step.IsTeardown,
                    error = s.Error,
                    // The second axis. `status` is what the request and its assertions did; this is
                    // what comparing the answer concluded, and a step can pass every assertion while
                    // differing from its snapshot.
                    comparison = s.Comparison.ToString().ToLowerInvariant(),
                    comparedAgainst = s.ComparedAgainst,
                    differences = s.DifferenceCount,
                    tolerated = s.ToleratedCount,
                    comparisonUnavailable = s.ComparisonUnavailableReason,
                    assertions = s.Assertions.Select(a => new
                    {
                        passed = a.Passed,
                        description = a.Description,
                        actual = a.Actual,
                    }),
                    // Captures carry only whether they worked and where they went. The VALUE is left
                    // out on purpose: the headline capture is an access token, and a report file is
                    // exactly the thing that gets attached to a build and kept.
                    captures = s.Captures.Select(c => new
                    {
                        ok = c.Ok,
                        variable = c.VariableName,
                        scope = c.Scope,
                        error = c.Error,
                    }),
                }),
            },
            Options);
    }
}

/// <summary>
/// JUnit XML - the format every CI system already renders.
///
/// <para>That is the whole reason it exists: a failed assertion becomes a failed test in the build's own
/// UI, with its message, instead of a line somewhere in a log nobody opens. The mapping is one
/// <c>testcase</c> per REQUEST rather than per assertion, because a request is the thing that has a
/// name, a duration and a URL - and a build page listing "status is 200" twenty times, once per
/// request, would name none of them.</para>
///
/// <para><b>A comparison that differs is a failed test here, exactly as it is in the exit code.</b>
/// This file used to map only <see cref="StepStatus"/>, so a run that found real drift between two
/// environments exited 1 while the report beside it said every test passed - and the build page, which
/// is the thing anyone actually looks at, went green. "Nothing to compare, therefore fine" is the
/// failure <see cref="RunReport.Ok"/> exists to refuse; a report that quietly disagrees with the
/// verdict it is reporting on is the same failure one file further out.</para>
/// </summary>
public static class JUnitRunReport
{
    public static string Write(RunReport report, string suiteName = "Fubar API Studio")
    {
        ArgumentNullException.ThrowIfNull(report);

        // A StringWriter that says UTF-8. XmlWriter takes the declared encoding from the writer it is
        // given, and a plain StringWriter reports UTF-16 (which is what a .NET string is) - so the file
        // would announce encoding="utf-16" while being written to disk as UTF-8. Strict XML parsers
        // reject that outright, which on a build agent looks like the report being corrupt rather than
        // mislabelled.
        var text = new Utf8StringWriter();
        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false };
        using var writer = XmlWriter.Create(text, settings);

        writer.WriteStartElement("testsuites");
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", suiteName);
        writer.WriteAttributeString("tests", report.Total.ToString());
        // Counted with the same predicate that decides whether to WRITE a <failure>, so the attribute
        // and the elements under it cannot drift apart.
        writer.WriteAttributeString("failures", report.Judged.Count(IsFailure).ToString());
        writer.WriteAttributeString("errors", report.Errored.ToString());
        writer.WriteAttributeString("skipped", report.Skipped.ToString());
        writer.WriteAttributeString("time", Seconds(report.ElapsedMilliseconds));

        foreach (var step in report.Steps)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("name", step.Step.QualifiedName);
            // The folder becomes the classname, so a CI page groups requests the way the collection does.
            writer.WriteAttributeString("classname", ClassNameFor(step));
            writer.WriteAttributeString("time", Seconds(step.ElapsedMilliseconds));

            if (step.Step.IsTeardown)
            {
                // Cleanup is never part of the verdict, so it is never a <failure> however it went -
                // it is named and described instead. A teardown that failed matters and is worth
                // reading; turning the build red for it would say the thing under test broke.
                Note(writer, CleanupNote(step));
            }
            else if (step.Status == StepStatus.Errored)
            {
                // An <error> rather than a <failure>: JUnit's distinction is exactly ours - a test
                // that ran and gave the wrong answer, versus one that never got to run.
                writer.WriteStartElement("error");
                writer.WriteAttributeString("message", step.Error ?? "No response");
                writer.WriteEndElement();
            }
            else if (IsFailure(step))
            {
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("message", FailureMessage(step));
                writer.WriteString(FailureDetail(step));
                writer.WriteEndElement();
            }
            else if (step.Status == StepStatus.Skipped)
            {
                writer.WriteStartElement("skipped");
                writer.WriteEndElement();
            }
            else
            {
                // Passed, with whatever is worth saying about how. system-out rather than a failure:
                // the run does not fail over a status nobody asserted on, and a report that said
                // otherwise would contradict the exit code sitting beside it.
                Note(writer, PassedNote(step));
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Flush();

        return text.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    /// <summary>A step the build should go red for. Two axes, not one: the assertions can pass while
    /// the answer differs from what it was compared against, and either one is a failed test.</summary>
    private static bool IsFailure(StepReport step) =>
        step.Status == StepStatus.Failed
        || step.Comparison is ComparisonVerdict.Differs or ComparisonVerdict.Unavailable;

    private static string FailureMessage(StepReport step) =>
        string.Join("; ", Reasons(step)) is { Length: > 0 } message ? message : "Failed";

    private static IEnumerable<string> Reasons(StepReport step)
    {
        if (step.AssertionsFailed > 0)
        {
            yield return Count(step.AssertionsFailed, "assertion") + " failed";
        }

        if (step.Comparison == ComparisonVerdict.Differs)
        {
            yield return $"{Count(step.DifferenceCount, "difference")} from {step.ComparedAgainst ?? "the other side"}";
        }

        if (step.Comparison == ComparisonVerdict.Unavailable)
        {
            // A missing other side is never a pass - see RunReport.Ok. Saying WHY matters here more
            // than anywhere: "no snapshot recorded" is fixed by recording one, and a build that only
            // said "failed" would send someone looking at the request instead.
            yield return step.ComparisonUnavailableReason ?? "nothing to compare against";
        }
    }

    private static string FailureDetail(StepReport step)
    {
        var lines = step.Assertions.Where(a => !a.Passed)
            .Select(a => a.Actual is { } actual ? $"{a.Description} — got {actual}" : a.Description)
            .ToList();

        lines.AddRange(step.ComparisonWarnings);

        // Empty when there is nothing to add: a body that only repeats the message costs a reader a
        // second look to find out it said nothing new. The count of differences is all a comparison
        // knows here - which paths differed lives in the run itself, not in the report.
        return string.Join(System.Environment.NewLine, lines);
    }

    /// <summary>What is worth saying about a step that passed. Nothing, usually.</summary>
    private static string? PassedNote(StepReport step)
    {
        var notes = new List<string>();

        if (step.IsUnexpectedStatus && step.Assertions.Count == 0)
        {
            notes.Add($"Responded {step.StatusCode} with no assertion to judge it.");
        }

        if (step.Comparison == ComparisonVerdict.Same && step.ComparedAgainst is { } against)
        {
            // A tolerance that forgave something is said out loud here for the same reason the console
            // says it: green and green-because-a-rule-allowed-it are different facts, and the second is
            // the one worth seeing when a tolerance turns out to be too generous.
            notes.Add(step.ToleratedCount > 0
                ? $"Matches {against} ({Count(step.ToleratedCount, "difference")} within tolerance)."
                : $"Matches {against}.");
        }

        notes.AddRange(step.ComparisonWarnings);

        return notes.Count > 0 ? string.Join(" ", notes) : null;
    }

    private static string CleanupNote(StepReport step) =>
        step.Status == StepStatus.Passed
            ? $"Cleanup responded {step.StatusCode}."
            : $"Cleanup did not complete: {step.Error ?? step.Status.ToString().ToLowerInvariant()}. Not part of the verdict.";

    private static void Note(XmlWriter writer, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        writer.WriteStartElement("system-out");
        writer.WriteString(text);
        writer.WriteEndElement();
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    private static string ClassNameFor(StepReport step)
    {
        var folder = Path.GetFileName(step.Step.FolderPath.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(folder) ? "collections" : folder;
    }

    /// <summary>JUnit times are seconds with a decimal point, and invariant - a machine reading this on
    /// a German build agent must not meet a comma.</summary>
    private static string Seconds(long milliseconds) =>
        (milliseconds / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
