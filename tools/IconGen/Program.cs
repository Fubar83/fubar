using System.Text;
using SkiaSharp;

// Generates both apps' icons and writes .ico / .icns / .png into each app's Assets folder.
// Run: dotnet run --project tools/IconGen
//
// One palette, two shapes. Both apps are obviously from the same family - the same off-white rounded
// tile, the same red - and the shape says which one you are looking at and what it does, which is what
// a launcher, a dock and a taskbar all need from an icon at 16 pixels.
//
// API Studio was a red letter "F", then two arrows. A letter names the product and says nothing about
// what it does - at 16px an F is any application starting with F - and the arrows read as a generic
// transfer or sync mark rather than as anything to do with an API. Braces say "JSON payload" to the
// people who use this, which is the one thing the app does all day. Fubar Diff had no icon AT ALL - its publish
// script has always copied src/Fubar.Diff.UI/Assets/fubar.icns, a path that did not exist, so every
// macOS build shipped with the generic application icon.

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

Write("Fubar.Studio.UI", DrawApiStudio);
Write("Fubar.Diff.UI", DrawDiff);

void Write(string project, Action<SKCanvas, float> draw)
{
    var assets = Path.Combine(repoRoot, "src", project, "Assets");
    Directory.CreateDirectory(assets);

    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024];
    var pngs = sizes.ToDictionary(s => s, s => RenderPng(s, draw, Tile.Light));

    File.WriteAllBytes(Path.Combine(assets, "fubar-256.png"), pngs[256]);

    // A dark-mode tile for the places THIS app draws its own mark - the title bar. An off-white
    // plaque on a near-black title bar is the brightest thing in the window, which is precisely
    // backwards for a decoration.
    //
    // Only the in-app copy: the .ico and .icns below are what Windows and macOS show in a taskbar,
    // a dock and an alt-tab card, and neither OS switches an application icon by theme. One icon is
    // all they will ever ask for, and it has to hold up on a light background too.
    File.WriteAllBytes(Path.Combine(assets, "fubar-256-dark.png"), RenderPng(256, draw, Tile.Dark));
    File.WriteAllBytes(Path.Combine(assets, "fubar.ico"), BuildIco([16, 24, 32, 48, 64, 128, 256], pngs));
    File.WriteAllBytes(Path.Combine(assets, "fubar.icns"), BuildIcns(pngs));

    Console.WriteLine($"Wrote fubar.ico, fubar.icns, fubar-256.png to {assets}");
}

static byte[] RenderPng(int size, Action<SKCanvas, float> draw, Tile tile_)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    float s = size;
    var tile = new SKRect(s * 0.04f, s * 0.04f, s * 0.96f, s * 0.96f);
    float radius = s * 0.22f;

    // Rounded tile with a faint border, so the icon reads on whatever sits behind it.
    using (var bg = new SKPaint { IsAntialias = true, Color = tile_.Fill })
    {
        canvas.DrawRoundRect(tile, radius, radius, bg);
    }

    using (var border = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1f, s * 0.012f),
        Color = tile_.Border,
    })
    {
        canvas.DrawRoundRect(tile, radius, radius, border);
    }

    draw(canvas, s);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// API Studio: curly braces - a JSON payload.
//
// What every developer reads as "the body of an API call" without being told, which is what this app
// spends its whole life sending and reading. It also stays itself at 16px, where the detail of a more
// pictorial mark would go: two shapes, a gap down the middle, nothing to lose.
//
// Drawn as a stroked path with round caps and joins rather than as text, so it does not depend on a
// font being present or on two platforms agreeing what a brace looks like - the same reason the rest
// of this file builds its shapes from geometry.
static void DrawApiStudio(SKCanvas canvas, float s)
{
    using var red = new SKPaint
    {
        IsAntialias = true,
        Color = new SKColor(0xE1, 0x1D, 0x2A),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = s * 0.085f,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    Brace(canvas, red, s, outerX: 0.40f, tipX: 0.245f, y0: 0.235f, y1: 0.765f);
    Brace(canvas, red, s, outerX: 0.60f, tipX: 0.755f, y0: 0.235f, y1: 0.765f);
}

/// One brace: out at the ends, in to a point at the middle. <paramref name="outerX"/> is the open end
/// and <paramref name="tipX"/> the pinch, so the same routine draws both by swapping them over.
static void Brace(SKCanvas canvas, SKPaint paint, float s, float outerX, float tipX, float y0, float y1)
{
    var mid = (y0 + y1) / 2;
    var spineX = (outerX + tipX) / 2;
    var r = (y1 - y0) * 0.22f;

    using var path = new SKPath();
    path.MoveTo(outerX * s, y0 * s);
    path.QuadTo(spineX * s, y0 * s, spineX * s, (y0 + r) * s);
    path.LineTo(spineX * s, (mid - r * 0.55f) * s);
    path.QuadTo(spineX * s, mid * s, tipX * s, mid * s);
    path.QuadTo(spineX * s, mid * s, spineX * s, (mid + r * 0.55f) * s);
    path.LineTo(spineX * s, (y1 - r) * s);
    path.QuadTo(spineX * s, y1 * s, outerX * s, y1 * s);

    canvas.DrawPath(path, paint);
}

// Fubar Diff: two versions of the same thing side by side, with one line that differs.
//
// Two columns of bars is the silhouette of every side-by-side diff ever drawn, and it survives being
// shrunk to 16px as two columns of dashes - which still reads as "a comparison" once the detail is
// gone. The difference is carried by one bar being visibly SHORTER on the right, because a length is
// still legible at 16px where a second colour is not.
static void DrawDiff(SKCanvas canvas, float s)
{
    var crimson = new SKColor(0xE1, 0x1D, 0x2A);
    using var red = new SKPaint { IsAntialias = true, Color = crimson };
    using var ghost = new SKPaint { IsAntialias = true, Color = crimson.WithAlpha(0x4D) };

    float rr = s * 0.025f;
    SKRect N(float x0, float y0, float x1, float y1) => new(x0 * s, y0 * s, x1 * s, y1 * s);

    float[] rows = [0.275f, 0.44f, 0.605f];
    const float Height = 0.115f;

    // Left column: three full-width bars - the original.
    foreach (var y in rows)
    {
        canvas.DrawRoundRect(N(0.20f, y, 0.455f, y + Height), rr, rr, red);
    }

    // Right column: the same three, with the middle one short. One line changed, which is the whole
    // idea; the ghosted remainder shows how far it used to reach.
    canvas.DrawRoundRect(N(0.545f, rows[0], 0.80f, rows[0] + Height), rr, rr, red);
    canvas.DrawRoundRect(N(0.545f, rows[1], 0.665f, rows[1] + Height), rr, rr, red);
    canvas.DrawRoundRect(N(0.685f, rows[1], 0.80f, rows[1] + Height), rr, rr, ghost);
    canvas.DrawRoundRect(N(0.545f, rows[2], 0.80f, rows[2] + Height), rr, rr, red);
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

/// The tile behind the glyph. The glyph itself is the same red in both: it is legible on either
/// ground, and an icon whose MARK changed colour with the theme would read as two different apps.
record Tile(SKColor Fill, SKColor Border)
{
    public static Tile Light { get; } = new(new SKColor(0xFD, 0xFD, 0xFD), new SKColor(0xE3, 0xE5, 0xEA));

    // Sits just above the dark title bar it lands on (BgHeader is #1E1E22), so the tile reads as a
    // plaque rather than as a hole.
    public static Tile Dark { get; } = new(new SKColor(0x2A, 0x2A, 0x31), new SKColor(0x3A, 0x3A, 0x44));
}

