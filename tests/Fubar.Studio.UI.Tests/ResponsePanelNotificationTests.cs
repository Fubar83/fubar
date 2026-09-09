using System.ComponentModel;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The response strip's derived text.
///
/// <para>Every member here is computed from an <c>[ObservableProperty]</c> and bound in
/// <c>ResponsePanelView</c>. The source generator raises the backing property and nothing else, so a
/// missing <c>NotifyPropertyChangedFor</c> is invisible in the code and permanent on screen: the
/// status log said "201 Created - 66 ms" while the pane beside it said 0 ms, 0 B, on every send.</para>
///
/// <para>These assert the notification rather than the formatting, because the formatting was never
/// the part that broke.</para>
/// </summary>
public class ResponsePanelNotificationTests
{
    private static ResponsePanelViewModel Panel() =>
        new(new NoClipboard(), new NoPicker(), new StatusLogViewModel(),
            new NoSchema(), new NoJsonPath(), new Fubar.Studio.UI.Services.ResponseBaselineService(), new NoDiffPreview());

    private static List<string> Changes(ResponsePanelViewModel panel, Action<ResponsePanelViewModel> act)
    {
        var raised = new List<string>();
        ((INotifyPropertyChanged)panel).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        act(panel);
        return raised;
    }

    [Fact]
    public void Timing_updates_when_the_response_does() =>
        Assert.Contains(
            nameof(ResponsePanelViewModel.ElapsedTimeText),
            Changes(Panel(), p => p.ElapsedMilliseconds = 66));

    [Fact]
    public void Size_updates_when_the_response_does() =>
        Assert.Contains(
            nameof(ResponsePanelViewModel.ContentSizeText),
            Changes(Panel(), p => p.SizeBytes = 88));

    [Fact]
    public void The_status_glyph_updates_with_the_status() =>
        Assert.Contains(
            nameof(ResponsePanelViewModel.StatusIcon),
            Changes(Panel(), p => p.StatusCode = 201));

    [Fact]
    public void The_content_type_line_and_the_image_preview_flag_update_together()
    {
        var raised = Changes(Panel(), p => p.ContentType = "image/png");

        Assert.Contains(nameof(ResponsePanelViewModel.ContentTypeHeader), raised);
        Assert.Contains(nameof(ResponsePanelViewModel.HasPreview), raised);
    }

    [Fact]
    public void Switching_view_updates_the_tree_flag() =>
        Assert.Contains(
            nameof(ResponsePanelViewModel.IsTreeViewSelected),
            Changes(Panel(), p => p.SelectedViewIndex = 1));

    // ---- Doubles ---------------------------------------------------------------------------------

    private sealed class NoClipboard : Fubar.Studio.UI.Services.IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class NoPicker : Fubar.Studio.UI.Services.IFilePickerService
    {
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickOpenFileAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoSchema : Fubar.Studio.Core.Json.IJsonSchemaValidator
    {
        public IReadOnlyList<string> Validate(string schemaJson, string bodyJson) => [];
    }

    private sealed class NoJsonPath : Fubar.Studio.Core.Json.IJsonPathEvaluator
    {
        public Fubar.Studio.Core.Json.JsonPathQueryResult Evaluate(string bodyJson, string expression) =>
            new([], null);
    }

    private sealed class NoDiffPreview : Fubar.Studio.UI.Services.IDiffPreviewService
    {
        public Task ShowAsync(
            string leftText, string rightText, string leftLabel, string rightLabel, string title,
            Fubar.Studio.UI.Services.DiffSettingsContext? settings = null) => Task.CompletedTask;
    }
}
