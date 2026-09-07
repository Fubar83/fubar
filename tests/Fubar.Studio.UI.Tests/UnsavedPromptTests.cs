using Fubar.Controls;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Switching request replaces the single canvas, so a dirty editor's changes are DESTROYED by it. It
/// used to write a line to the status log - collapsed by default, with Ctrl+` the only way in - and
/// carry on, so the sole notice of losing work went somewhere invisible. Closing the window did not
/// even do that: there was no Closing handler at all.
///
/// <para>The rule pinned here is the one Fubar Diff already states and tests: anything that is not an
/// explicit answer means KEEP. It is reachable three ways - a dismissed dialog, no window to be modal
/// to, and a save that failed - and treating any of them as consent to discard is exactly the bug a
/// confirmation exists to prevent.</para>
/// </summary>
public class UnsavedPromptTests
{
    private static Task<bool> SaveSucceeds() => Task.FromResult(true);

    private static Task<bool> SaveFails() => Task.FromResult(false);

    [Fact]
    public async Task A_clean_editor_is_replaced_without_asking()
    {
        var confirmation = new ScriptedConfirmation(-1);

        var choice = await UnsavedChangesPrompt.AskAsync(isDirty: false, "Orders", confirmation, SaveSucceeds);

        Assert.Equal(UnsavedChoice.Proceed, choice);
        Assert.Equal(0, confirmation.TimesAsked);
    }

    [Fact]
    public async Task Dismissing_the_prompt_keeps_the_edits()
    {
        var confirmation = new ScriptedConfirmation(-1);

        var choice = await UnsavedChangesPrompt.AskAsync(isDirty: true, "Orders", confirmation, SaveSucceeds);

        Assert.Equal(UnsavedChoice.Keep, choice);
        Assert.Equal(1, confirmation.TimesAsked);
    }

    [Fact]
    public async Task Discard_goes_ahead()
    {
        var choice = await UnsavedChangesPrompt.AskAsync(isDirty: true, "Orders", new ScriptedConfirmation(1), SaveSucceeds);

        Assert.Equal(UnsavedChoice.Proceed, choice);
    }

    [Fact]
    public async Task Save_goes_ahead_once_the_editor_is_clean()
    {
        var saved = false;

        var choice = await UnsavedChangesPrompt.AskAsync(isDirty: true, "Orders", new ScriptedConfirmation(0), () =>
        {
            saved = true;
            return Task.FromResult(true);
        });

        Assert.Equal(UnsavedChoice.Proceed, choice);
        Assert.True(saved);
    }

    /// <summary>
    /// The subtle one. The user asked to KEEP the edits by choosing Save; if the save then failed -
    /// a read-only file, a full disk - proceeding anyway would discard exactly what they were trying
    /// to protect, and the failure message would scroll past in the log behind the new editor.
    /// </summary>
    [Fact]
    public async Task A_failed_save_does_not_proceed()
    {
        var choice = await UnsavedChangesPrompt.AskAsync(isDirty: true, "Orders", new ScriptedConfirmation(0), SaveFails);

        Assert.Equal(UnsavedChoice.Keep, choice);
    }

    /// <summary>
    /// With no way to ask, the answer is still keep. Refusing to switch is recoverable; silently
    /// destroying an edit is not, so a missing dependency must fail safe rather than fall through to
    /// the old behaviour.
    /// </summary>
    [Fact]
    public async Task With_no_way_to_ask_nothing_is_discarded()
    {
        var choice = await UnsavedChangesPrompt.AskAsync(isDirty: true, "Orders", confirmation: null, SaveSucceeds);

        Assert.Equal(UnsavedChoice.Keep, choice);
    }

    /// <summary>The question has to name the editor - "you have unsaved changes" in an app that shows
    /// one thing at a time still leaves the user guessing which one.</summary>
    [Fact]
    public async Task The_question_names_the_editor()
    {
        var confirmation = new ScriptedConfirmation(-1);

        await UnsavedChangesPrompt.AskAsync(isDirty: true, "Create order", confirmation, SaveSucceeds);

        Assert.Contains("Create order", confirmation.LastTitle, StringComparison.Ordinal);
    }

    /// <summary>Answers from a script, and records what it was asked - so a test can assert a clean
    /// editor was never asked about at all.</summary>
    private sealed class ScriptedConfirmation(int answer) : IConfirmationService
    {
        public int TimesAsked { get; private set; }

        public string LastTitle { get; private set; } = "";

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
            Task.FromResult(answer == 0);

        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices)
        {
            TimesAsked++;
            LastTitle = title;
            return Task.FromResult(answer);
        }

        public Task<string?> AskForTextAsync(string title, string message, string initial = "") =>
            Task.FromResult<string?>(null);
    }
}
