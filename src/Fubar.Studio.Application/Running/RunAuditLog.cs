using System.Text.Json;
using System.Text.Json.Serialization;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Running;

/// <summary>
/// Appends one JSON line per collection run: who, where, against what, and the verdict.
///
/// <para>The honest answer to "who ran what against production", and deliberately a small one. It is
/// built ON the rolling log rather than instead of it - there was no application log at all, so an
/// audit feature would have been a claim with nothing underneath. Off unless a path is given, because
/// an audit trail nobody asked for is just a file that grows.</para>
///
/// <para><b>No captured values, no request bodies, no response bodies.</b> The whole point of the run
/// report omitting capture values applies here with more force: this file is written to be kept.</para>
/// </summary>
public static class RunAuditLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Appends one line describing <paramref name="report"/>. Never throws: an audit write that took
    /// down the run it was recording would be a worse failure than the one it exists to catch.
    /// </summary>
    /// <returns>The problem, or null when the line was written.</returns>
    public static string? Append(string path, RunReport report, Workspace workspace, WorkspaceEnvironment? environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);

        try
        {
            var line = JsonSerializer.Serialize(
                new
                {
                    at = DateTimeOffset.UtcNow,
                    user = Environment.UserName,
                    host = Environment.MachineName,
                    workspace = workspace.Manifest.Name,
                    workspaceId = workspace.WorkspaceId,
                    environment = environment?.Name,
                    ok = report.Ok,
                    total = report.Total,
                    passed = report.Passed,
                    failed = report.Failed,
                    errored = report.Errored,
                    skipped = report.Skipped,
                    elapsedMs = report.ElapsedMilliseconds,
                    cancelled = report.WasCancelled,
                    // The requests that ran, by name and outcome - enough to answer "what was sent",
                    // and nothing that could carry a credential.
                    steps = report.Steps.Select(s => new
                    {
                        name = s.Step.Name,
                        status = s.Status.ToString().ToLowerInvariant(),
                        statusCode = s.StatusCode,
                    }),
                },
                Options);

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // One JSON object per line - the shape every log shipper already reads, and appendable
            // without rewriting what is there.
            File.AppendAllText(path, line + System.Environment.NewLine);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex.Message;
        }
    }
}
