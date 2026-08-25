using System.Text;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Infrastructure.Files;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Save fidelity, checked at the BYTE level. Comparing decoded strings would pass even if the writer
/// silently added a BOM or rewrote every terminator - which is exactly the failure this guards, since
/// it turns a one-line merge into a whole-file diff in the user's version control.
/// </summary>
public class TextFileWriterTests : IDisposable
{
    private readonly TextFileWriter _writer = new();
    private readonly TextFileReader _reader = new();
    private readonly List<string> _temporaryFiles = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Most of these assert exact bytes, so they pin the terminator behaviour explicitly rather than
    /// inheriting the default (which DOES add a trailing newline - see the round-trip tests).
    /// </summary>
    private static readonly TextFormat NoTrailingNewline = TextFormat.Default with { EndsWithNewline = false };

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fubar-diff-write-{Guid.NewGuid():N}.txt");
        _temporaryFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _temporaryFiles)
        {
            try { File.Delete(path); } catch (IOException) { /* best effort cleanup */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Writes_lf_terminators()
    {
        var path = TempPath();

        await _writer.WriteAsync(path, ["a", "b"], NoTrailingNewline, Token);

        Assert.Equal("a\nb"u8.ToArray(), await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task Writes_crlf_terminators()
    {
        var path = TempPath();
        var format = NoTrailingNewline with { LineEnding = LineEnding.Crlf };

        await _writer.WriteAsync(path, ["a", "b"], format, Token);

        Assert.Equal("a\r\nb"u8.ToArray(), await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task Does_not_add_a_bom_when_the_source_had_none()
    {
        var path = TempPath();

        await _writer.WriteAsync(path, ["hello"], NoTrailingNewline, Token);

        var bytes = await File.ReadAllBytesAsync(path, Token);
        Assert.Equal((byte)'h', bytes[0]);
    }

    [Fact]
    public async Task Preserves_a_utf8_bom_when_the_source_had_one()
    {
        var path = TempPath();
        var format = NoTrailingNewline with { HasByteOrderMark = true };

        await _writer.WriteAsync(path, ["hello"], format, Token);

        var bytes = await File.ReadAllBytesAsync(path, Token);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public async Task Round_trips_a_file_through_read_and_write_unchanged()
    {
        // The strongest statement of the contract: read a file, write it straight back, and the bytes
        // must be identical. A save with no merge decisions does exactly this.
        var path = TempPath();
        var original = new UTF8Encoding(true).GetBytes("first\r\nsecond\r\nthird");
        await File.WriteAllBytesAsync(path, original, Token);

        var document = await _reader.ReadAsync(path, Token);
        await _writer.WriteAsync(path, document.Lines, document.Format, Token);

        Assert.Equal(original, await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task Round_trips_a_trailing_newline()
    {
        // Regression: the reader drops the empty string after a final newline, so an early version
        // saved the file back without it - which git reports as "\ No newline at end of file" on the
        // last line, turning a one-line merge into a two-line diff.
        var path = TempPath();
        var original = "alpha\nbeta\n"u8.ToArray();
        await File.WriteAllBytesAsync(path, original, Token);

        var document = await _reader.ReadAsync(path, Token);
        Assert.True(document.Format.EndsWithNewline);

        await _writer.WriteAsync(path, document.Lines, document.Format, Token);

        Assert.Equal(original, await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task Round_trips_the_absence_of_a_trailing_newline()
    {
        var path = TempPath();
        var original = "alpha\nbeta"u8.ToArray();
        await File.WriteAllBytesAsync(path, original, Token);

        var document = await _reader.ReadAsync(path, Token);
        Assert.False(document.Format.EndsWithNewline);

        await _writer.WriteAsync(path, document.Lines, document.Format, Token);

        Assert.Equal(original, await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task Round_trips_utf16()
    {
        var path = TempPath();
        var original = new List<byte>(Encoding.Unicode.GetPreamble());
        original.AddRange(Encoding.Unicode.GetBytes("høy\nda"));
        await File.WriteAllBytesAsync(path, [.. original], Token);

        var document = await _reader.ReadAsync(path, Token);
        await _writer.WriteAsync(path, document.Lines, document.Format, Token);

        Assert.Equal(original, await File.ReadAllBytesAsync(path, Token));
    }

    [Fact]
    public async Task Overwrites_an_existing_file()
    {
        var path = TempPath();
        await File.WriteAllTextAsync(path, "much longer original content", Token);

        await _writer.WriteAsync(path, ["short"], NoTrailingNewline, Token);

        Assert.Equal("short", await File.ReadAllTextAsync(path, Token));
    }

    [Fact]
    public async Task Leaves_no_temporary_file_behind()
    {
        var path = TempPath();

        await _writer.WriteAsync(path, ["a"], NoTrailingNewline, Token);

        Assert.False(File.Exists(path + ".fubardiff.tmp"));
    }

    [Fact]
    public async Task An_unwritable_path_reports_a_readable_reason()
    {
        // A directory that does not exist - the closest portable stand-in for "cannot write here".
        var path = Path.Combine(Path.GetTempPath(), $"no-such-dir-{Guid.NewGuid():N}", "out.txt");

        var ex = await Assert.ThrowsAsync<TextFileWriteException>(
            () => _writer.WriteAsync(path, ["a"], TextFormat.Default, Token));

        Assert.Equal(path, ex.Path);
    }

    [Fact]
    public async Task Writing_nothing_produces_an_empty_file()
    {
        var path = TempPath();

        await _writer.WriteAsync(path, [], TextFormat.Default, Token);

        Assert.Empty(await File.ReadAllBytesAsync(path, Token));
    }
}
