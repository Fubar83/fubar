using System.Text;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Infrastructure.Files;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Encoding, line-ending, and rejection behaviour. Uses real temporary files: the whole point of this
/// adapter is its interaction with the file system, so faking that away would test nothing.
/// </summary>
public class TextFileReaderTests : IDisposable
{
    private readonly TextFileReader _reader = new();
    private readonly List<string> _temporaryFiles = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string WriteFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fubar-diff-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, bytes);
        _temporaryFiles.Add(path);
        return path;
    }

    private string WriteText(string text, Encoding? encoding = null) =>
        WriteFile((encoding ?? new UTF8Encoding(false)).GetBytes(text));

    public void Dispose()
    {
        foreach (var path in _temporaryFiles)
        {
            try { File.Delete(path); } catch (IOException) { /* best effort cleanup */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Splits_on_lf()
    {
        var document = await _reader.ReadAsync(WriteText("a\nb\nc"), Token);

        Assert.Equal(["a", "b", "c"], document.Lines);
        Assert.Equal(LineEnding.Lf, document.Format.LineEnding);
    }

    [Fact]
    public async Task Splits_on_crlf_and_reports_it()
    {
        var document = await _reader.ReadAsync(WriteText("a\r\nb"), Token);

        Assert.Equal(["a", "b"], document.Lines);
        Assert.Equal(LineEnding.Crlf, document.Format.LineEnding);
    }

    [Fact]
    public async Task A_trailing_newline_does_not_add_an_empty_line()
    {
        // "a\n" is one line in every editor; a naive Split would report two.
        var document = await _reader.ReadAsync(WriteText("a\n"), Token);

        Assert.Equal(["a"], document.Lines);
    }

    [Fact]
    public async Task An_empty_file_has_no_lines()
    {
        var document = await _reader.ReadAsync(WriteText(""), Token);

        Assert.Empty(document.Lines);
    }

    [Fact]
    public async Task A_utf8_bom_is_stripped_rather_than_becoming_a_stray_character()
    {
        var document = await _reader.ReadAsync(WriteText("hello", new UTF8Encoding(true)), Token);

        Assert.Equal(["hello"], document.Lines);
    }

    [Fact]
    public async Task Utf16_is_decoded_via_its_bom()
    {
        var bytes = new List<byte>(Encoding.Unicode.GetPreamble());
        bytes.AddRange(Encoding.Unicode.GetBytes("høy\nda"));

        var document = await _reader.ReadAsync(WriteFile([.. bytes]), Token);

        Assert.Equal(["høy", "da"], document.Lines);
    }

    [Fact]
    public async Task A_missing_file_reports_a_readable_reason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");

        var ex = await Assert.ThrowsAsync<TextFileReadException>(() => _reader.ReadAsync(path, Token));
        Assert.Contains("does not exist", ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_binary_file_is_rejected_rather_than_rendered_as_mojibake()
    {
        var path = WriteFile([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0xFF, 0x01]);

        var ex = await Assert.ThrowsAsync<TextFileReadException>(() => _reader.ReadAsync(path, Token));
        Assert.Contains("binary", ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisplayName_is_the_file_name()
    {
        var path = WriteText("x");
        var document = await _reader.ReadAsync(path, Token);

        Assert.Equal(Path.GetFileName(path), document.DisplayName);
    }
}
