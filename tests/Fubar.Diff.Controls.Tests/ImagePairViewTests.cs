using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The side-by-side picture view.
///
/// Its main job here is to LOAD: a mistake in the XAML is a runtime failure with nothing to catch it
/// at build time, and this view is only reached by opening two images, which no other test does. What
/// it shows beyond that is the view model's business, and that is asserted directly.
/// </summary>
public class ImagePairViewTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static BinaryComparison Compare(byte[] left, byte[] right) =>
        BinaryComparer.Compare(
            new BinaryDocument("l", left, ImageFormatDetector.Detect(left)),
            new BinaryDocument("r", right, ImageFormatDetector.Detect(right)));

    [AvaloniaFact]
    public void The_view_loads_and_binds()
    {
        var model = new ImagePairViewModel();
        model.Show(Compare(Png, Png));

        var window = new Window { Content = new ImagePairView { DataContext = model }, Width = 800, Height = 400 };

        window.Show();
        window.UpdateLayout();

        Assert.NotNull(window.Content);
    }

    [AvaloniaFact]
    public void A_non_image_comparison_shows_nothing()
    {
        var model = new ImagePairViewModel();

        model.Show(Compare([0x4D, 0x5A], [0x4D, 0x5B]));

        Assert.False(model.HasImages);
        Assert.Empty(model.LeftCaption);
    }

    [AvaloniaFact]
    public void Null_clears_it()
    {
        var model = new ImagePairViewModel();
        model.Show(Compare(Png, Png));

        model.Show(null);

        Assert.False(model.HasImages);
        Assert.False(model.SameDimensions);
    }

    [AvaloniaFact]
    public void A_picture_that_will_not_decode_says_so_rather_than_throwing()
    {
        // A signature is not a guarantee the file is intact - a truncated download announces itself as
        // a PNG right up until the decoder gives up - and that has to degrade to a caption, not to an
        // exception out of a comparison.
        var model = new ImagePairViewModel();

        model.Show(Compare(Png, Png));

        Assert.Contains("PNG", model.LeftCaption, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Disposing_it_twice_is_safe()
    {
        // The tab disposes it, and a view model that has already been cleared is a perfectly ordinary
        // state to dispose from.
        var model = new ImagePairViewModel();
        model.Show(Compare(Png, Png));

        model.Dispose();
        model.Dispose();

        Assert.False(model.HasImages);
    }
}
