using System.Text;
using SkiaSharp;

// Generates the app icon (a bold red "F" on a rounded off-white tile) and writes .ico / .icns / .png
// into src/Fubar.Studio.UI/Assets. Run: dotnet run --project tools/IconGen

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var assets = Path.Combine(repoRoot, "src", "Fubar.Studio.UI", "Assets");
Directory.CreateDirectory(assets);

int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024 };
var pngs = sizes.ToDictionary(s => s, RenderPng);

File.WriteAllBytes(Path.Combine(assets, "fubar-256.png"), pngs[256]);
File.WriteAllBytes(Path.Combine(assets, "fubar.ico"), BuildIco([16, 24, 32, 48, 64, 128, 256], pngs));
File.WriteAllBytes(Path.Combine(assets, "fubar.icns"), BuildIcns(pngs));

Console.WriteLine($"Wrote fubar.ico, fubar.icns, fubar-256.png to {assets}");

static byte[] RenderPng(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    float s = size;
    var tile = new SKRect(s * 0.04f, s * 0.04f, s * 0.96f, s * 0.96f);
    float radius = s * 0.22f;

    // Rounded off-white tile with a faint border, so the icon reads on light AND dark backgrounds.
    using (var bg = new SKPaint { IsAntialias = true, Color = new SKColor(0xFD, 0xFD, 0xFD) })
    {
        canvas.DrawRoundRect(tile, radius, radius, bg);
    }

    using (var border = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1f, s * 0.012f),
        Color = new SKColor(0xE3, 0xE5, 0xEA),
    })
    {
        canvas.DrawRoundRect(tile, radius, radius, border);
    }

    // The red "F": stem + top bar + middle bar, built from rounded rects (font-independent).
    var red = new SKColor(0xE1, 0x1D, 0x2A);
    using var pen = new SKPaint { IsAntialias = true, Color = red };
    float rr = s * 0.02f;

    SKRect N(float x0, float y0, float x1, float y1) => new(x0 * s, y0 * s, x1 * s, y1 * s);
    canvas.DrawRoundRect(N(0.34f, 0.24f, 0.45f, 0.78f), rr, rr, pen); // stem
    canvas.DrawRoundRect(N(0.34f, 0.24f, 0.66f, 0.35f), rr, rr, pen); // top bar
    canvas.DrawRoundRect(N(0.34f, 0.45f, 0.60f, 0.545f), rr, rr, pen); // middle bar

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// Windows .ico as a container of PNG frames (Vista+; Avalonia reads these fine).
static byte[] BuildIco(int[] frameSizes, Dictionary<int, byte[]> pngs)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((ushort)0);                       // reserved
    w.Write((ushort)1);                       // type = icon
    w.Write((ushort)frameSizes.Length);

    var offset = 6 + 16 * frameSizes.Length;
    foreach (var size in frameSizes)
    {
        var png = pngs[size];
        w.Write((byte)(size >= 256 ? 0 : size)); // width  (0 => 256)
        w.Write((byte)(size >= 256 ? 0 : size)); // height
        w.Write((byte)0);                         // palette
        w.Write((byte)0);                         // reserved
        w.Write((ushort)1);                       // colour planes
        w.Write((ushort)32);                      // bits per pixel
        w.Write((uint)png.Length);
        w.Write((uint)offset);
        offset += png.Length;
    }

    foreach (var size in frameSizes)
    {
        w.Write(pngs[size]);
    }

    return ms.ToArray();
}

// Apple .icns as PNG-typed chunks (icp4=16, icp5=32, icp6=64, ic07=128, ic08=256, ic09=512, ic10=1024).
static byte[] BuildIcns(Dictionary<int, byte[]> pngs)
{
    (string Type, int Size)[] map =
    [
        ("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
    ];

    using var body = new MemoryStream();
    foreach (var (type, size) in map)
    {
        if (!pngs.TryGetValue(size, out var png))
        {
            continue;
        }

        body.Write(Encoding.ASCII.GetBytes(type));
        WriteBigEndian(body, (uint)(8 + png.Length));
        body.Write(png);
    }

    using var ms = new MemoryStream();
    ms.Write(Encoding.ASCII.GetBytes("icns"));
    WriteBigEndian(ms, (uint)(8 + body.Length));
    body.Position = 0;
    body.CopyTo(ms);
    return ms.ToArray();
}

static void WriteBigEndian(Stream stream, uint value)
{
    stream.WriteByte((byte)(value >> 24));
    stream.WriteByte((byte)(value >> 16));
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
}
