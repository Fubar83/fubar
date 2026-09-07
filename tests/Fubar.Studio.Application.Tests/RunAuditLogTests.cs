using System.Text.Json;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// "Who ran what against production" had no answer: history is per-request local JSON with no
/// identity, and the run report covers one run.
///
/// <para>The rule that matters most here is what it must NOT contain. This file is written to be kept -
/// which is precisely the argument JsonRunReport already makes for omitting capture values.</para>
/// </summary>
public class RunAuditLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "fubar-audit-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static Workspace Ws => new() { RootPath = "/w", Manifest = new AppManifest { Id = "ws1", Name = "Orders API" } };

    private static RunReport Report() => new(
        [
            new StepReport(
                new RunStep(1, "Login", "/w/collections/login.json", "/w/collections"),
                StepStatus.Passed,
                200,
                null,
                40,
                128,
                [],
                [],
                null),
        ],
        ElapsedMilliseconds: 120,
        WasCancelled: false,
        StoppedEarly: false);

    private string Path_ => Path.Combine(_directory, "audit.jsonl");

    [Fact]
    public void A_run_is_recorded_with_who_and_where()
    {
        Assert.Null(RunAuditLog.Append(Path_, Report(), Ws, new WorkspaceEnvironment { Name = "Production" }));

        var line = JsonDocument.Parse(File.ReadAllLines(Path_).Single()).RootElement;

        Assert.Equal("Orders API", line.GetProperty("workspace").GetString());
        Assert.Equal("Production", line.GetProperty("environment").GetString());
        Assert.False(string.IsNullOrEmpty(line.GetProperty("user").GetString()));
        Assert.False(string.IsNullOrEmpty(line.GetProperty("host").GetString()));
    }

    /// <summary>The whole point of the file. A body or a captured value here would be the leak this
    /// codebase has spent its effort closing, written on purpose and kept.</summary>
    [Fact]
    public void No_captured_value_or_body_reaches_the_audit_line()
    {
        RunAuditLog.Append(Path_, Report(), Ws, null);

        var text = File.ReadAllText(Path_);

        Assert.DoesNotContain("body", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capture", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One object per line, so a log shipper can read it and an append needs no rewrite.</summary>
    [Fact]
    public void Runs_append_one_line_each()
    {
        RunAuditLog.Append(Path_, Report(), Ws, null);
        RunAuditLog.Append(Path_, Report(), Ws, null);

        Assert.Equal(2, File.ReadAllLines(Path_).Length);
    }

    [Fact]
    public void The_directory_is_created_if_missing()
    {
        var nested = Path.Combine(_directory, "deep", "deeper", "audit.jsonl");

        Assert.Null(RunAuditLog.Append(nested, Report(), Ws, null));
        Assert.True(File.Exists(nested));
    }

    /// <summary>
    /// An audit write that took down the run it was recording would be a worse failure than the one it
    /// exists to catch, so the problem is returned rather than thrown.
    /// </summary>
    [Fact]
    public void An_unwritable_path_is_reported_rather_than_thrown()
    {
        Directory.CreateDirectory(_directory);
        var occupied = Path.Combine(_directory, "occupied");
        File.WriteAllText(occupied, "");

        var problem = RunAuditLog.Append(Path.Combine(occupied, "audit.jsonl"), Report(), Ws, null);

        Assert.NotNull(problem);
    }
}
