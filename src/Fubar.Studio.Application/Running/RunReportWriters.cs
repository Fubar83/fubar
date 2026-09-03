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
                assertionsPassed = report.AssertionsPassed,
                assertionsFailed = report.AssertionsFailed,
                elapsedMs = report.ElapsedMilliseconds,
                cancelled = report.WasCancelled,
                stoppedEarly = report.StoppedEarly,
                steps = report.Steps.Select(s => new
                {
                    order = s.Step.Order,
                    name = s.Step.Name,
                    path = s.Step.FilePath,
                    status = s.Status.ToString().ToLowerInvariant(),
                    statusCode = s.StatusCode,
                    elapsedMs = s.ElapsedMilliseconds,
                    sizeBytes = s.SizeBytes,
                    // Reported alongside the status rather than folded into it, exactly as in the UI.
                    unexpectedStatus = s.IsUnexpectedStatus && s.Assertions.Count == 0,
                    error = s.Error,
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
        writer.WriteAttributeString("failures", report.Failed.ToString());
        writer.WriteAttributeString("errors", report.Errored.ToString());
        writer.WriteAttributeString("skipped", report.Skipped.ToString());
        writer.WriteAttributeString("time", Seconds(report.ElapsedMilliseconds));

        foreach (var step in report.Steps)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("name", step.Step.Name);
            // The folder becomes the classname, so a CI page groups requests the way the collection does.
            writer.WriteAttributeString("classname", ClassNameFor(step));
            writer.WriteAttributeString("time", Seconds(step.ElapsedMilliseconds));

            switch (step.Status)
            {
                case StepStatus.Failed:
                    writer.WriteStartElement("failure");
                    writer.WriteAttributeString("message", FailureMessage(step));
                    writer.WriteString(string.Join(
                        System.Environment.NewLine,
                        step.Assertions.Where(a => !a.Passed)
                            .Select(a => a.Actual is { } actual ? $"{a.Description} — got {actual}" : a.Description)));
                    writer.WriteEndElement();
                    break;

                case StepStatus.Errored:
                    // An <error> rather than a <failure>: JUnit's distinction is exactly ours - a test
                    // that ran and gave the wrong answer, versus one that never got to run.
                    writer.WriteStartElement("error");
                    writer.WriteAttributeString("message", step.Error ?? "No response");
                    writer.WriteEndElement();
                    break;

                case StepStatus.Skipped:
                    writer.WriteStartElement("skipped");
                    writer.WriteEndElement();
                    break;

                case StepStatus.Passed when step.IsUnexpectedStatus && step.Assertions.Count == 0:
                    // Passed, and worth saying why it might not look like it. system-out rather than a
                    // failure: the run does not fail over a status nobody asserted on, and a report that
                    // said otherwise would contradict the exit code sitting beside it.
                    writer.WriteStartElement("system-out");
                    writer.WriteString($"Responded {step.StatusCode} with no assertion to judge it.");
                    writer.WriteEndElement();
                    break;
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

    private static string FailureMessage(StepReport step) =>
        step.AssertionsFailed == 1
            ? "1 assertion failed"
            : $"{step.AssertionsFailed} assertions failed";

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
