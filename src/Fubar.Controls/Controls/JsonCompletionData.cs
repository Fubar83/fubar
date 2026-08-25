using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace Fubar.Controls;

/// <summary>One row in the JSON editor's schema completion list. <see cref="Complete"/> replaces the
/// segment the list was filtering (from the trigger offset to the caret) with the candidate's insert
/// text, so a picked property lands as <c>"name": </c> and an enum value closes its own quote.</summary>
internal sealed class JsonCompletionData : ICompletionData
{
    private readonly string _insertText;

    public JsonCompletionData(CompletionCandidate candidate)
    {
        Text = candidate.FilterText;
        Content = candidate.Display;
        Description = candidate.Description ?? candidate.Display;
        _insertText = candidate.InsertText;
    }

    public IImage? Image => null;

    public string Text { get; }

    public object Content { get; }

    public object Description { get; }

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, _insertText);
}
