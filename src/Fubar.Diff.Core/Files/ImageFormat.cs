using System;

namespace Fubar.Diff.Core.Files;

/// <summary>An image container this app is willing to display. <see cref="None"/> means "not an image".</summary>
public enum ImageFormat
{
    None,
    Png,
    Jpeg,
    Gif,
    Bmp,
    Webp,
    Ico,
}

/// <summary>
/// Recognises an image from its own first bytes.
///
/// From the CONTENT, not the extension, and for the opposite reason that the language detector reads
/// the extension instead. A language cannot be told from source text reliably enough to be worth
/// guessing at, and being wrong there quietly changes what counts as a difference. An image container
/// announces itself in a fixed signature at offset zero - that is what the signature is for - and being
/// wrong is immediately visible, because the picture either appears or it does not. A `.png` that is
/// really a JPEG is a real thing that happens; a JPEG that does not start with its own marker is not.
/// </summary>
public static class ImageFormatDetector
{
    /// <summary>The format the bytes announce, or <see cref="ImageFormat.None"/>.</summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> bytes)
    {
        if (StartsWith(bytes, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ImageFormat.Png;
        }

        // SOI, then any marker. The third byte is not checked: it identifies the first segment (JFIF,
        // Exif, or a bare quantisation table from an encoder that writes neither) and pinning it would
        // reject perfectly ordinary files.
        if (StartsWith(bytes, [0xFF, 0xD8, 0xFF]))
        {
            return ImageFormat.Jpeg;
        }

        if (StartsWith(bytes, "GIF87a"u8) || StartsWith(bytes, "GIF89a"u8))
        {
            return ImageFormat.Gif;
        }

        if (StartsWith(bytes, "BM"u8))
        {
            return ImageFormat.Bmp;
        }

        // A RIFF container that says WEBP at offset 8; the four bytes between are the file size.
        if (StartsWith(bytes, "RIFF"u8) && bytes.Length >= 12 && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return ImageFormat.Webp;
        }

        // Type 1 is an icon, type 2 a cursor - both are ICO containers and both render.
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] is 1 or 2 && bytes[3] == 0)
        {
            return ImageFormat.Ico;
        }

        return ImageFormat.None;
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> signature) =>
        bytes.Length >= signature.Length && bytes[..signature.Length].SequenceEqual(signature);
}
