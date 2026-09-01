using Fubar.Diff.Core.Files;
using Fubar.Diff.Infrastructure.Files;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Reading a file as bytes, and the hand-off from the text reader that gets us there.
/// </summary>
public class BinaryFileReaderTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("fubar-binary-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test is a nuisance, not a failure.
        }
    }

    private string Write(string name, params byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    [Fact]
    public async Task The_bytes_come_back_exactly_as_written()
    {
        var path = Write("a.bin", 1, 2, 3, 0, 255);

        var document = await new BinaryFileReader().ReadAsync(path, Token);

        Assert.Equal([1, 2, 3, 0, 255], document.Bytes.ToArray());
        Assert.Equal(5, document.Length);
        Assert.Equal(path, document.Path);
    }

    [Fact]
    public async Task An_image_is_recognised_on_the_way_in()
    {
        var path = Write("a.png", 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2);

        var document = await new BinaryFileReader().ReadAsync(path, Token);

        Assert.Equal(ImageFormat.Png, document.Format);
        Assert.True(document.IsImage);
    }

    [Fact]
    public async Task A_file_named_png_that_is_not_one_is_not_treated_as_an_image()
    {
        // The signature decides, not the extension. A renamed file is a real thing, and handing a
        // decoder something that is not a picture is how you get an exception instead of a comparison.
        var path = Write("lies.png", 0x4D, 0x5A, 0x90, 0x00);

        var document = await new BinaryFileReader().ReadAsync(path, Token);

        Assert.Equal(ImageFormat.None, document.Format);
    }

    [Fact]
    public async Task A_missing_file_reports_the_same_way_the_text_reader_does()
    {
        var reader = new BinaryFileReader();

        var ex = await Assert.ThrowsAsync<TextFileReadException>(
            () => reader.ReadAsync(Path.Combine(_folder, "nope.bin"), Token));

        Assert.False(ex.IsBinary);
    }

    [Fact]
    public async Task The_text_reader_says_WHY_it_refused_a_binary_file()
    {
        // The flag, not the wording. A caller matching on the message would silently stop offering
        // binary comparison the day someone improved the sentence.
        var path = Write("a.bin", 0x00, 0x01, 0x02);

        var ex = await Assert.ThrowsAsync<TextFileReadException>(
            () => new TextFileReader().ReadAsync(path, Token));

        Assert.True(ex.IsBinary);
    }

    [Fact]
    public async Task A_missing_text_file_is_not_flagged_as_binary()
    {
        // The distinction the fallback turns on: one of these should become a byte comparison and the
        // other must stay an error.
        var ex = await Assert.ThrowsAsync<TextFileReadException>(
            () => new TextFileReader().ReadAsync(Path.Combine(_folder, "nope.txt"), Token));

        Assert.False(ex.IsBinary);
    }

    [Fact]
    public async Task A_utf16_file_is_still_read_as_text()
    {
        var path = Write("utf16.txt", 0xFF, 0xFE, 0x48, 0x00, 0x69, 0x00);

        var document = await new TextFileReader().ReadAsync(path, Token);

        Assert.Equal(["Hi"], document.Lines);
    }
}
