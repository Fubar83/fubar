using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Controls.ViewModels;

/// <summary>
/// Two images, side by side, for a comparison of files that turned out to be pictures.
///
/// This is the one case where a diff tool should stop diffing and just show the thing. Nobody reading
/// a changed icon wants a byte offset; they want to see the two icons. The hex view is still there
/// underneath for when the question really is about the bytes.
/// </summary>
public sealed partial class ImagePairViewModel : ObservableObject, IDisposable
{
    /// <summary>The left picture, or null when it could not be decoded.</summary>
    [ObservableProperty]
    public partial Bitmap? Left { get; set; }

    /// <summary>The right picture.</summary>
    [ObservableProperty]
    public partial Bitmap? Right { get; set; }

    /// <summary>The left image's dimensions and format, for the caption under it.</summary>
    [ObservableProperty]
    public partial string LeftCaption { get; set; } = string.Empty;

    /// <summary>The right image's caption.</summary>
    [ObservableProperty]
    public partial string RightCaption { get; set; } = string.Empty;

    /// <summary>
    /// True when the two pictures are the same size in pixels.
    ///
    /// Worth saying out loud, because it is the difference between "this icon was redrawn" and "this
    /// icon was rescaled", and the two look identical side by side at whatever size the pane gives
    /// them.
    /// </summary>
    [ObservableProperty]
    public partial bool SameDimensions { get; set; }

    /// <summary>True once there is at least one picture to show.</summary>
    public bool HasImages => Left is not null || Right is not null;

    /// <summary>
    /// Decodes both sides, or clears everything when handed null.
    ///
    /// Decoding failures are swallowed per side. The format was recognised from a signature, which is
    /// not the same as the file being intact - a truncated download announces itself as a PNG right up
    /// until the decoder gives up - and one unreadable side must still leave the other on screen, since
    /// "this one is corrupt and that one is not" is a perfectly good answer to what changed.
    /// </summary>
    public void Show(BinaryComparison? comparison)
    {
        Clear();

        if (comparison is null || !comparison.BothAreImages)
        {
            OnPropertyChanged(nameof(HasImages));
            return;
        }

        Left = Decode(comparison.Left.Bytes);
        Right = Decode(comparison.Right.Bytes);

        LeftCaption = Caption(Left, comparison.Left.Format.ToString().ToUpperInvariant());
        RightCaption = Caption(Right, comparison.Right.Format.ToString().ToUpperInvariant());

        SameDimensions = Left is not null && Right is not null && Left.PixelSize == Right.PixelSize;

        OnPropertyChanged(nameof(HasImages));
    }

    /// <summary>Releases both bitmaps. They hold unmanaged decoding buffers.</summary>
    public void Dispose() => Clear();

    private void Clear()
    {
        Left?.Dispose();
        Right?.Dispose();

        Left = null;
        Right = null;
        LeftCaption = string.Empty;
        RightCaption = string.Empty;
        SameDimensions = false;
    }

    private static Bitmap? Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            // A copy, because MemoryStream over the shared buffer would tie the bitmap's lifetime to
            // bytes the comparison also owns - and Bitmap reads the stream during construction only,
            // so the stream itself can go immediately.
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);

            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // Broadly, deliberately: decoders throw whatever their backend throws, and a picture that
            // will not open must degrade to the hex view rather than take the window down.
            return null;
        }
    }

    private static string Caption(Bitmap? bitmap, string format) =>
        bitmap is null
            ? $"{format} - could not be displayed"
            : $"{format} - {bitmap.PixelSize.Width} x {bitmap.PixelSize.Height}";
}
