using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// What happens when a comparison turns out not to be of text.
///
/// The behaviour worth pinning is the hand-off: a file the text reader refuses becomes a BYTE
/// comparison rather than an error, both sides are read that way even if only one was binary, and the
/// result still looks like an ordinary comparison to everything that consumes one.
/// </summary>
public class BinaryFallbackTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A text reader that refuses the paths it was told are binary.</summary>
    private sealed class StubTextReader(params string[] binaryPaths) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (binaryPaths.Contains(path, StringComparer.Ordinal))
            {
                throw new TextFileReadException(path, "it appears to be a binary file.") { IsBinary = true };
            }

            return Task.FromResult(new TextDocument(path, ["line"], TextFormat.Default));
        }
    }

    private sealed class StubBinaryReader(Dictionary<string, byte[]> files) : IBinaryFileReader
    {
        public int Reads { get; private set; }

        public Task<BinaryDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            Reads++;

            var bytes = files[path];

            return Task.FromResult(new BinaryDocument(path, bytes, ImageFormatDetector.Detect(bytes)));
        }
    }

    private static FileComparisonService Build(ITextFileReader text, IBinaryFileReader? binary) => new(
        text,
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()),
        binary);

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public async Task A_binary_file_becomes_a_byte_comparison_rather_than_an_error()
    {
        var service = Build(
            new StubTextReader("l.bin", "r.bin"),
            new StubBinaryReader(new() { ["l.bin"] = [1, 2, 3], ["r.bin"] = [1, 9, 3] }));

        var comparison = await service.CompareFilesAsync("l.bin", "r.bin", new ComparisonOptions(), Token);

        Assert.True(comparison.IsBinary);
        Assert.Equal(1, comparison.Binary!.FirstDifference);
    }

    [Fact]
    public async Task One_binary_side_still_reads_BOTH_as_bytes()
    {
        // A PNG against a text file is still a pair the user asked about, and the only comparison of it
        // that means anything is at the byte level. "The left one is binary" and nothing else would be
        // accurate and useless.
        var binary = new StubBinaryReader(new() { ["l.png"] = Png, ["r.txt"] = "hello"u8.ToArray() });
        var service = Build(new StubTextReader("l.png"), binary);

        var comparison = await service.CompareFilesAsync("l.png", "r.txt", new ComparisonOptions(), Token);

        Assert.True(comparison.IsBinary);
        Assert.Equal(2, binary.Reads);
        Assert.False(comparison.Binary!.BothAreImages);
    }

    [Fact]
    public async Task Without_a_binary_reader_the_refusal_stands()
    {
        // The degradation, not a crash: a host that never wired one up behaves exactly as it did
        // before binary comparison existed.
        var service = Build(new StubTextReader("l.bin", "r.bin"), binary: null);

        await Assert.ThrowsAsync<TextFileReadException>(
            () => service.CompareFilesAsync("l.bin", "r.bin", new ComparisonOptions(), Token));
    }

    [Fact]
    public async Task A_missing_file_is_still_an_error_and_not_a_byte_comparison()
    {
        var service = Build(new ThrowingReader(), new StubBinaryReader([]));

        await Assert.ThrowsAsync<TextFileReadException>(
            () => service.CompareFilesAsync("gone.txt", "gone.txt", new ComparisonOptions(), Token));
    }

    private sealed class ThrowingReader : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            throw new TextFileReadException(path, "the file does not exist.");
    }

    [Fact]
    public async Task The_result_still_carries_both_paths()
    {
        // Everything in the UI that names a comparison, watches its files or lists it as recent reads
        // these, and none of it has any business knowing the content was not text.
        var service = Build(
            new StubTextReader("l.bin", "r.bin"),
            new StubBinaryReader(new() { ["l.bin"] = [1], ["r.bin"] = [2] }));

        var comparison = await service.CompareFilesAsync("l.bin", "r.bin", new ComparisonOptions(), Token);

        Assert.Equal("l.bin", comparison.Left.Path);
        Assert.Equal("r.bin", comparison.Right.Path);
        Assert.True(comparison.HasBothSides);

        // And no text rows: there is nothing to align, and inventing some would put a diff on screen
        // that means nothing.
        Assert.Empty(comparison.Result.Lines);
    }

    [Fact]
    public async Task Two_images_are_recognised_as_such()
    {
        var service = Build(
            new StubTextReader("l.png", "r.png"),
            new StubBinaryReader(new() { ["l.png"] = Png, ["r.png"] = [.. Png, 4] }));

        var comparison = await service.CompareFilesAsync("l.png", "r.png", new ComparisonOptions(), Token);

        Assert.True(comparison.Binary!.BothAreImages);
    }

    [Fact]
    public async Task Recomparing_a_binary_result_keeps_it_binary()
    {
        // The nasty one. A binary result carries EMPTY text documents, so re-running the text path over
        // them succeeds, produces an empty diff and drops the byte comparison - the tab would quietly
        // turn from a picture into "the files are identical" the moment anyone ticked an option.
        var service = Build(
            new StubTextReader("l.bin", "r.bin"),
            new StubBinaryReader(new() { ["l.bin"] = [1], ["r.bin"] = [2] }));

        var comparison = await service.CompareFilesAsync("l.bin", "r.bin", new ComparisonOptions(), Token);

        var again = await service.RecompareAsync(
            comparison, new ComparisonOptions { IgnoreWhitespace = true }, Token);

        Assert.True(again.IsBinary);
        Assert.Same(comparison.Binary, again.Binary);
        Assert.True(again.Options.IgnoreWhitespace);

        // The synchronous overload takes the same path.
        Assert.True(service.Recompare(comparison, new ComparisonOptions()).IsBinary);
    }
}
