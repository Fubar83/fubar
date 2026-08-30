using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using Fubar.Diff.Core.Editing;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.Controls.Rendering;
using TextMateSharp.Grammars;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// One side of the side-by-side view: a read-only <see cref="TextEditor"/> wired to the three diff
/// renderers (line tint, character spans, source-number gutter).
///
/// Exists as its own control so the two panes are provably symmetrical - left and right differ only in
/// which <see cref="AlignedDocument"/> they are given - and so <c>DiffView</c> stays about layout and
/// scroll sync rather than rendering detail.
/// </summary>
public partial class DiffEditorPane : UserControl
{
    /// <summary>The flattened document for this side. Setting it re-renders the pane.</summary>
    public static readonly StyledProperty<AlignedDocument?> DocumentProperty =
        AvaloniaProperty.Register<DiffEditorPane, AlignedDocument?>(nameof(Document));

    /// <summary>
    /// True when this pane is a close-up (<c>DiffDetailPane</c>) rather than one of the main
    /// side-by-side panes - see <see cref="DiffLineColors.LineBackground"/> for why that changes the
    /// tint's intensity.
    /// </summary>
    public static readonly StyledProperty<bool> EmphasizedProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(Emphasized));

    /// <summary>Whether to mark invisible characters - see <see cref="InvisibleCharacterGenerator"/>.</summary>
    public static readonly StyledProperty<bool> ShowInvisiblesProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(ShowInvisibles));

    /// <summary>
    /// Whether the user can type into this pane.
    ///
    /// Off by default, and every close-up, the unified view, the Json panes and the hex view leave it
    /// off - they show a document that is not a file, or a file that must not be written back as text.
    /// Only the two main side-by-side panes turn it on.
    ///
    /// What it costs is filler tracking: the document is the file with blank rows interleaved, so an
    /// editable pane has to be able to hand back the file's own lines afterwards. See
    /// <see cref="ReadFileLines"/>.
    /// </summary>
    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(IsEditable));

    /// <summary>
    /// Whether long lines wrap rather than scrolling horizontally.
    ///
    /// Only ever set by the unified view. Turning it on for one of the side-by-side panes would break
    /// the row-count parity the two columns are aligned by - a wrapped line occupies two visual lines
    /// on one side and one on the other, and the panes drift apart by a line for every wrap above the
    /// viewport.
    /// </summary>
    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(WordWrap));

    /// <summary>
    /// The file extension whose grammar this side should be highlighted with (<c>.cs</c>, <c>.ts</c>),
    /// or null for none.
    ///
    /// An extension rather than a <c>SourceLanguage</c> on purpose: the highlighter and the COMPARISON
    /// know different amounts. The comparison only claims a language when it has real scanning rules
    /// for it, which is a short list; highlighting works for anything TextMate ships a grammar for,
    /// which is a long one - and a Python file gets nothing from the code-comparison rules but is still
    /// far easier to read coloured. Tying the two together would mean either colouring nothing outside
    /// the short list, or claiming to compare languages we cannot scan.
    /// </summary>
    public static readonly StyledProperty<string?> SyntaxExtensionProperty =
        AvaloniaProperty.Register<DiffEditorPane, string?>(nameof(SyntaxExtension));

    /// <summary>Whether syntax highlighting is wanted at all. On by default.</summary>
    public static readonly StyledProperty<bool> SyntaxHighlightingProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(SyntaxHighlighting), defaultValue: true);

    /// <summary>
    /// Stretches of unchanged context to hide behind a collapsed placeholder, as ROW ranges - see
    /// <see cref="CollapsedRegions"/>, which computes them.
    ///
    /// Both panes are given the SAME ranges, which is what keeps them aligned: identical folds over
    /// documents that already have identical row counts means identical visual line counts, so scroll
    /// sync stays the plain offset copy it has always been rather than becoming a mapping problem.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<FoldRange>?> FoldsProperty =
        AvaloniaProperty.Register<DiffEditorPane, IReadOnlyList<FoldRange>?>(nameof(Folds));

    /// <summary>
    /// The grammar and theme registry, shared by every pane in the process.
    ///
    /// One instance, deliberately: constructing it reads TextMateSharp's grammar and theme resources,
    /// and there are at least four panes on screen (two main, two in the close-up) plus one per open
    /// tab. Per-pane instances would repeat that work for identical data. It is only created when a
    /// pane is actually asked to highlight something, so a session comparing nothing but text files
    /// never pays for it at all.
    /// </summary>
    private static RegistryOptions? _registry;

    private static RegistryOptions Registry => _registry ??= new RegistryOptions(ThemeName.DarkPlus);

    private TextMate.Installation? _textMate;

    /// <summary>Installed the first time this pane is asked to fold anything. See <see cref="ApplyFolds"/>.</summary>
    private FoldingManager? _foldings;

    /// <summary>The scope currently installed, so re-applying the same grammar is free.</summary>
    private string? _grammarScope;

    private readonly ChangeLineBackgroundRenderer _backgroundRenderer;
    private readonly CharSpanColorizer _colorizer;
    private readonly SourceLineNumberMargin _lineNumbers;
    private readonly CurrentHunkRenderer _currentHunk;
    private readonly InvisibleCharacterGenerator _invisibles;
    private readonly SearchPanel _searchPanel;

    /// <summary>
    /// One anchor per filler row, so the file's own lines can be recovered after arbitrary editing.
    ///
    /// Anchors rather than a running offset map, because AvaloniaEdit already maintains them through
    /// every insertion, deletion and replacement - including ones that destroy the anchored line, which
    /// it reports rather than silently mis-answering. Measured at 0.031 ms per keystroke with 6,000 of
    /// them on a 60,000-line document, which is why this is affordable at all.
    /// </summary>
    private readonly List<TextAnchor> _fillerAnchors = [];

    /// <summary>
    /// Filler layouts for documents this pane has actually shown, keyed by their exact text.
    ///
    /// This exists because of UNDO, and it is the one part of the mechanism that is not obvious.
    /// Anchors follow the user's own edits perfectly, including when those edits are undone - an
    /// anchor created before an edit is put back by the undo, because an undo is just another text
    /// change. What anchors cannot survive is the app RE-ANCHORING mid-history, which is exactly what
    /// re-aligning after each edit does: undo past a re-alignment and the anchors describe a layout
    /// the document no longer has, so a blank filler row reads as a blank line the user typed and the
    /// file quietly grows one.
    ///
    /// An undo lands on a document this pane has shown before, so recognising it by text is enough to
    /// answer correctly without trusting the anchors at all. Bounded, because the alternative is
    /// keeping every revision of a large file in memory for the life of the tab.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<bool>> _knownAlignments = new(StringComparer.Ordinal);

    private readonly Queue<string> _knownOrder = new();

    /// <summary>How many past layouts to recognise, and how large a document is worth remembering.</summary>
    private const int RememberedAlignments = 64;

    private const int RememberedTextLimit = 1024 * 1024;

    /// <summary>
    /// True while the app is changing the document itself, so its own writes are not mistaken for the
    /// user's. Without it, re-aligning after an edit would look like another edit.
    /// </summary>
    private bool _applying;

    public DiffEditorPane()
    {
        InitializeComponent();

        _backgroundRenderer = new ChangeLineBackgroundRenderer(this);
        _colorizer = new CharSpanColorizer(this);
        _lineNumbers = new SourceLineNumberMargin();
        _currentHunk = new CurrentHunkRenderer(this);
        _invisibles = new InvisibleCharacterGenerator();

        Editor.TextArea.TextView.BackgroundRenderers.Add(_backgroundRenderer);
        // AFTER the change tint, deliberately: same layer, and background renderers paint in
        // registration order, so the current-hunk marker must be added second to land on top.
        Editor.TextArea.TextView.BackgroundRenderers.Add(_currentHunk);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.ElementGenerators.Add(_invisibles);
        Editor.TextArea.LeftMargins.Add(_lineNumbers);

        // Ctrl+F within a pane. AvaloniaEdit brings its own search panel, so this is one line rather
        // than a find bar of our own - and it searches the pane the caret is in, which is what a user
        // pressing Ctrl+F in a two-pane view means.
        _searchPanel = SearchPanel.Install(Editor);

        // Replace comes with the search panel once the editor is writable, so the find bar grows a
        // second row on its own rather than needing anything here.
        Editor.Document.TextChanged += OnDocumentTextChanged;

        // Per pane rather than routed from the window, unlike the gutter below: DiffView only forwards
        // theme changes to the two MAIN panes, so the close-up's editors would keep dark-theme token
        // colours on a light background after a switch.
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();

        ApplyGutterStyle();
    }

    public AlignedDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public bool Emphasized
    {
        get => GetValue(EmphasizedProperty);
        set => SetValue(EmphasizedProperty, value);
    }

    public bool ShowInvisibles
    {
        get => GetValue(ShowInvisiblesProperty);
        set => SetValue(ShowInvisiblesProperty, value);
    }

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>
    /// Raised when the USER changed this pane's text - never for the app's own updates.
    ///
    /// The distinction is the whole reason this exists rather than the host subscribing to the
    /// editor: re-aligning after an edit changes the document too, and a host that could not tell the
    /// two apart would re-diff its own re-diff, forever.
    /// </summary>
    public event EventHandler? Edited;

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public string? SyntaxExtension
    {
        get => GetValue(SyntaxExtensionProperty);
        set => SetValue(SyntaxExtensionProperty, value);
    }

    public bool SyntaxHighlighting
    {
        get => GetValue(SyntaxHighlightingProperty);
        set => SetValue(SyntaxHighlightingProperty, value);
    }

    public IReadOnlyList<FoldRange>? Folds
    {
        get => GetValue(FoldsProperty);
        set => SetValue(FoldsProperty, value);
    }

    /// <summary>The underlying editor, for the parent view to wire scroll sync and caret moves.</summary>
    internal TextEditor TextEditor => Editor;

    /// <summary>Opens the find bar for this pane.</summary>
    internal void OpenSearch()
    {
        Editor.Focus();
        _searchPanel.Open();
    }

    /// <summary>The text view, which owns the scroll offset the two panes keep in step.</summary>
    internal TextView TextView => Editor.TextArea.TextView;

    /// <summary>
    /// Marks a row range as the current difference, or clears it with a negative start. Redraws
    /// immediately - this is driven by navigation, where the whole point is instant feedback.
    ///
    /// Also tells the background tint and character-span colourizer which rows are "current", so rows
    /// outside the range can fade - see <see cref="ChangeLineBackgroundRenderer.SetCurrentRange"/>.
    /// </summary>
    internal void SetCurrentHunk(int startIndex, int endIndex)
    {
        _currentHunk.SetRange(startIndex, endIndex);
        _backgroundRenderer.SetCurrentRange(startIndex, endIndex);
        _colorizer.SetCurrentRange(startIndex, endIndex);
        Editor.TextArea.TextView.Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
        {
            Apply(change.GetNewValue<AlignedDocument?>());
        }
        else if (change.Property == EmphasizedProperty)
        {
            var emphasized = change.GetNewValue<bool>();
            _backgroundRenderer.SetEmphasized(emphasized);
            _colorizer.SetEmphasized(emphasized);
            Editor.TextArea.TextView.Redraw();
        }
        else if (change.Property == ShowInvisiblesProperty)
        {
            // Redraw, not re-render: the generator is consulted per visual line, so invalidating the
            // visual lines is what makes it run again.
            _invisibles.SetEnabled(change.GetNewValue<bool>());
            Editor.TextArea.TextView.Redraw();
        }
        else if (change.Property == IsEditableProperty)
        {
            Editor.IsReadOnly = !change.GetNewValue<bool>();
        }
        else if (change.Property == WordWrapProperty)
        {
            // The editor recomputes its visual lines itself; the renderers all key off VisualLine,
            // whose Height already covers every wrapped row of a document line, so they need nothing.
            Editor.WordWrap = change.GetNewValue<bool>();
        }
        else if (change.Property == SyntaxExtensionProperty || change.Property == SyntaxHighlightingProperty)
        {
            ApplySyntax();
        }
        else if (change.Property == FoldsProperty)
        {
            ApplyFolds();
        }
        else if (change.Property == ForegroundProperty || change.Property == FontSizeProperty)
        {
            ApplyGutterStyle();
        }
    }

    /// <summary>
    /// Installs (or clears) the grammar for the current extension.
    ///
    /// TextMate is installed on FIRST USE rather than in the constructor: installing adds a line
    /// transformer and pulls in the registry, and the majority of comparisons - JSON, logs, config,
    /// plain text - never need one. A pane that is never asked to highlight stays exactly as cheap as
    /// it was before this existed.
    /// </summary>
    private void ApplySyntax()
    {
        var scope = SyntaxHighlighting ? ScopeFor(SyntaxExtension) : null;

        if (scope is null)
        {
            // Not uninstalled, just blanked: an installation that has already run has already added its
            // transformer, and removing it mid-session is more surface than turning the grammar off.
            // Only when there IS one to clear - the library resolves the scope name through its
            // registry, and handing it a null to resolve is not a documented no-op.
            if (_grammarScope is not null)
            {
                Clear();
            }

            return;
        }

        if (_textMate is null)
        {
            _textMate = Editor.InstallTextMate(Registry);
            ApplyEditorTheme();
        }

        if (_grammarScope == scope)
        {
            return;
        }

        _textMate.SetGrammar(scope);
        _grammarScope = scope;
    }

    /// <summary>Turns the highlighter off, leaving the pane's own diff rendering untouched.</summary>
    private void Clear()
    {
        try
        {
            _textMate?.SetGrammar(null);
        }
        catch (Exception)
        {
            // See ScopeFor: a highlighter problem must never be the reason a diff cannot be read.
        }

        _grammarScope = null;
    }

    /// <summary>
    /// The TextMate scope for a file extension, or null when there is no grammar for it - which is a
    /// perfectly ordinary outcome, not an error: the file is simply shown uncoloured.
    ///
    /// Broadly caught, deliberately. This is a reading aid layered on top of the thing the user
    /// actually opened the app for; a grammar that fails to resolve has to degrade to plain text, not
    /// take the pane - and with it the diff - down with it.
    /// </summary>
    private static string? ScopeFor(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;

        try
        {
            var scope = Registry.GetScopeByExtension(normalized);

            return string.IsNullOrEmpty(scope) ? null : scope;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Points the highlighter at the theme the app is actually wearing. Token colours chosen for a dark
    /// background are close to invisible on a light one, so this is not cosmetic.
    /// </summary>
    private void ApplyEditorTheme()
    {
        if (_textMate is null)
        {
            return;
        }

        var dark = ActualThemeVariant == ThemeVariant.Dark;

        _textMate.SetTheme(Registry.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
    }

    private void Apply(AlignedDocument? document)
    {
        var lines = document?.Lines ?? [];

        // Metadata BEFORE text: setting the text triggers a render pass, and a renderer holding the
        // previous comparison's line list would paint the old tints for one frame.
        _backgroundRenderer.SetLines(lines);
        _colorizer.SetLines(lines);
        _lineNumbers.SetLines(lines);

        // A row range from the previous comparison addresses rows this document may not have.
        _currentHunk.SetRange(-1, -1);
        _backgroundRenderer.SetCurrentRange(-1, -1);
        _colorizer.SetCurrentRange(-1, -1);

        _applying = true;
        try
        {
            // A re-alignment after the user's own edit is patched in rather than replaced, so their
            // caret, selection and undo history survive it. Everything else - a new comparison, a
            // changed option, a reload - replaces the document outright, which is both simpler and
            // correct, because none of those leave the user mid-sentence.
            if (!TryRealign(document))
            {
                Editor.Document.Text = document?.Text ?? string.Empty;

                // Loading a document is not an edit, and must not be undoable: without this, Ctrl+Z
                // in a freshly opened comparison walks back past the file being loaded and empties
                // the pane. It also resets the history between comparisons, which is right - undoing
                // into the previous pair's text would be nonsense.
                Editor.Document.UndoStack.ClearAll();

                // Scroll home: the previous offset means nothing in a document just replaced.
                Editor.ScrollToHome();
            }

            TrackFillers(lines);
        }
        finally
        {
            _applying = false;
        }

        // After the text, not before: a fold is a pair of document OFFSETS, and the offsets of the
        // previous comparison's document mean nothing in this one.
        ApplyFolds();

        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Re-anchors the filler rows against whatever the document now holds.
    ///
    /// Anchors from the previous alignment are dropped rather than reused: they were placed against a
    /// document that no longer exists, and an anchor pointing at the wrong line would quietly delete
    /// the wrong text on the next reconstruction.
    /// </summary>
    private void TrackFillers(IReadOnlyList<AlignedLine> lines)
    {
        _fillerAnchors.Clear();

        if (!IsEditable)
        {
            // A read-only pane never has to hand its content back, so it pays nothing for this.
            return;
        }

        var document = Editor.Document;
        var flags = FillerPatch.FillerFlags(lines);

        for (var i = 0; i < lines.Count && i < document.LineCount; i++)
        {
            if (!flags[i])
            {
                continue;
            }

            var anchor = document.CreateAnchor(document.GetLineByNumber(i + 1).Offset);

            // A filler destroyed by an edit must report itself as gone, not drift to a neighbouring
            // line - the text that replaced it is the user's and has to be kept.
            anchor.SurviveDeletion = false;

            _fillerAnchors.Add(anchor);
        }

        Remember(document.Text, flags);
    }

    /// <summary>Records a layout this pane has shown, so an undo back to it can be recognised.</summary>
    private void Remember(string text, IReadOnlyList<bool> flags)
    {
        if (text.Length > RememberedTextLimit || !_knownAlignments.TryAdd(text, flags))
        {
            return;
        }

        _knownOrder.Enqueue(text);

        while (_knownOrder.Count > RememberedAlignments)
        {
            _knownAlignments.Remove(_knownOrder.Dequeue());
        }
    }

    /// <summary>
    /// The line numbers that are fillers, as the document stands now.
    ///
    /// A layout this pane has shown before is answered from the record of it rather than from the
    /// anchors - see <see cref="_knownAlignments"/> for why that matters after an undo. Anything else
    /// is the user part-way through an edit, which is precisely what anchors are good at.
    /// </summary>
    private HashSet<int> LiveFillerLines()
    {
        if (_knownAlignments.TryGetValue(Editor.Document.Text, out var known))
        {
            return [.. FillerPatch.FillerLines(known)];
        }

        var lines = new HashSet<int>();

        foreach (var anchor in _fillerAnchors)
        {
            if (!anchor.IsDeleted)
            {
                lines.Add(anchor.Line);
            }
        }

        return lines;
    }

    /// <summary>
    /// This pane's content as the FILE's own lines - the fillers removed, the user's edits kept.
    ///
    /// The one rule, applied by <see cref="AlignedEdit.ToFileLines"/>: a line belongs to the file
    /// unless it is empty AND still a filler. Everything a person does while editing falls out of it,
    /// including typing into a filler, which is how a line is added where the other side already has
    /// one.
    /// </summary>
    public IReadOnlyList<string> ReadFileLines()
    {
        var document = Editor.Document;

        // An empty document still reports one (empty) line, so without this a comparison of nothing
        // would hand back a file containing a blank line. Whether a file is zero bytes or a single
        // newline is carried by TextFormat.EndsWithNewline, not by this list.
        if (document.TextLength == 0)
        {
            return [];
        }

        var documentLines = new string[document.LineCount];

        for (var i = 0; i < documentLines.Length; i++)
        {
            documentLines[i] = document.GetText(document.GetLineByNumber(i + 1));
        }

        return AlignedEdit.ToFileLines(documentLines, LiveFillerLines());
    }

    /// <summary>
    /// Moves this pane's filler rows to where the new alignment wants them, leaving the user's own
    /// text - and their place in it - alone. Returns false when that cannot be done, so the caller
    /// falls back to replacing the document.
    ///
    /// Only ever a re-alignment: the file's lines are identical either side of one, because the new
    /// alignment was computed from this very document. <see cref="FillerPatch"/> checks that rather
    /// than assuming it, and refuses if the premise has broken - losing the caret is a nuisance,
    /// losing a line of the user's code is not.
    /// </summary>
    private bool TryRealign(AlignedDocument? wanted)
    {
        var document = Editor.Document;

        if (!IsEditable || wanted is null || document.TextLength == 0)
        {
            return false;
        }

        var wantedFlags = FillerPatch.FillerFlags(wanted.Lines);
        var wantedText = wanted.Text.Split('\n');

        if (wantedText.Length != wanted.Lines.Count)
        {
            return false;
        }

        // The premise, checked rather than assumed: a re-alignment moves blank rows around text that
        // has not changed. If the file's own lines differ, this is a different comparison arriving -
        // and patching one into the other would silently keep the old content while claiming to show
        // the new.
        if (!SameFileLines(ReadFileLines(), wantedText, wantedFlags))
        {
            return false;
        }

        var fillers = LiveFillerLines();

        var current = new bool[document.LineCount];
        for (var i = 0; i < current.Length; i++)
        {
            current[i] = document.GetLineByNumber(i + 1).Length == 0 && fillers.Contains(i + 1);
        }

        if (FillerPatch.Compute(current, wantedFlags) is not { } edits)
        {
            return false;
        }

        if (edits.Count == 0)
        {
            return true;
        }

        // Where the caret is in the FILE, which is the only coordinate that means the same thing
        // before and after. Restoring it by raw offset looks right and is not: the text moves around
        // the offset, and the caret silently ends up on a different line.
        var caret = Editor.TextArea.Caret;
        var fileLine = AlignedEdit.ToFileLine(caret.Line, fillers);
        var column = caret.Column;

        // Folded into the undo entry for the keystroke that caused it, so one Ctrl+Z takes back the
        // edit AND the re-alignment it triggered. Its own group would make the user press Ctrl+Z twice
        // for one change, and swapping the undo stack out to hide it destroys the stack outright.
        var undo = document.UndoStack;

        // NOT wrapped in BeginUpdate/EndUpdate, and that is not an oversight: those start an undo
        // group of their own, which nests inside this one and breaks the continuation - the
        // re-alignment then becomes a separate entry and Ctrl+Z takes two presses for one change. The
        // edits are a handful of blank lines, so there is nothing worth batching anyway.
        if (undo.CanUndo)
        {
            undo.StartContinuedUndoGroup();
        }
        else
        {
            undo.StartUndoGroup();
        }

        try
        {
            foreach (var edit in edits)
            {
                if (edit.LineNumber < 1 || edit.LineNumber > document.LineCount + 1)
                {
                    continue;
                }

                if (edit.Kind == FillerEditKind.InsertBlank)
                {
                    var offset = edit.LineNumber > document.LineCount
                        ? document.TextLength
                        : document.GetLineByNumber(edit.LineNumber).Offset;

                    document.Insert(offset, "\n");
                }
                else if (edit.LineNumber <= document.LineCount)
                {
                    var line = document.GetLineByNumber(edit.LineNumber);

                    // A last line has no terminator of its own, so removing it has to take the one
                    // BEFORE it or the file grows a trailing blank line every time.
                    if (line.TotalLength == line.Length && line.Offset > 0)
                    {
                        document.Remove(line.Offset - 1, line.Length + 1);
                    }
                    else
                    {
                        document.Remove(line.Offset, line.TotalLength);
                    }
                }
            }
        }
        finally
        {
            undo.EndUndoGroup();
        }

        RestoreCaret(fileLine, column, wantedFlags);

        return true;
    }

    /// <summary>
    /// Whether an alignment describes the same file this pane already holds - its non-filler rows, in
    /// order, being exactly the lines currently on screen once the fillers are taken out.
    /// </summary>
    private static bool SameFileLines(
        IReadOnlyList<string> fileLines,
        IReadOnlyList<string> alignedText,
        IReadOnlyList<bool> fillerFlags)
    {
        var next = 0;

        for (var i = 0; i < alignedText.Count; i++)
        {
            if (fillerFlags[i])
            {
                continue;
            }

            if (next >= fileLines.Count || !string.Equals(fileLines[next], alignedText[i], StringComparison.Ordinal))
            {
                return false;
            }

            next++;
        }

        return next == fileLines.Count;
    }

    /// <summary>Puts the caret back at the file position it was at, in the new alignment.</summary>
    private void RestoreCaret(int fileLine, int column, IReadOnlyList<bool> fillerFlags)
    {
        var document = Editor.Document;
        var documentLine = AlignedEdit.ToDocumentLine(fileLine, FillerPatch.FillerLines(fillerFlags), document.LineCount);

        var line = document.GetLineByNumber(documentLine);

        Editor.TextArea.Caret.Line = documentLine;
        Editor.TextArea.Caret.Column = column > line.Length + 1 ? line.Length + 1 : column;
    }

    /// <summary>
    /// Replaces a range of ROWS with the given lines, as an ordinary edit.
    ///
    /// This is how taking a side works. It could have been done by rewriting the file and re-comparing,
    /// but then it would not be on the editor's undo stack - and the whole reason for making a merge an
    /// edit is that Ctrl+Z takes it back like everything else. Going through the document also means
    /// the normal cycle follows on its own: the change is reported, the comparison re-runs, and the
    /// difference disappears.
    ///
    /// Rows, not file lines, because that is the coordinate a hunk speaks: row i is document line i+1
    /// in both panes, which is the invariant the whole side-by-side view rests on.
    /// </summary>
    public void ReplaceRows(int firstRow, int lastRow, IReadOnlyList<string> lines)
    {
        var document = Editor.Document;

        if (firstRow < 0 || lastRow < firstRow || firstRow >= document.LineCount)
        {
            return;
        }

        var first = document.GetLineByNumber(firstRow + 1);
        var last = document.GetLineByNumber(Math.Min(lastRow + 1, document.LineCount));

        var replacement = string.Join("\n", lines);

        // Keep the block a block: replacing up to EndOffset leaves the terminator that follows it, so
        // the lines after this one stay where they are instead of being pulled up into it.
        document.Replace(first.Offset, last.EndOffset - first.Offset, replacement);
    }

    /// <summary>Reports the user's own edits, and only those - see <see cref="Edited"/>.</summary>
    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        if (!_applying && IsEditable)
        {
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Collapses the given row ranges, or clears every fold when there are none.
    ///
    /// The folding manager is installed on FIRST USE, like the highlighter and for the same reason:
    /// installing adds a margin to the editor, and a pane that is never asked to fold anything - the
    /// two close-ups never are - should not grow one.
    /// </summary>
    private void ApplyFolds()
    {
        var ranges = Folds;

        if (ranges is null || ranges.Count == 0)
        {
            _foldings?.Clear();
            return;
        }

        _foldings ??= FoldingManager.Install(Editor.TextArea);

        var document = Editor.Document;
        var foldings = new List<NewFolding>(ranges.Count);

        foreach (var range in ranges)
        {
            // Rows are 0-based and AvaloniaEdit's lines are 1-based. Clamped rather than trusted: a
            // fold list can arrive a frame before the document it was computed for.
            var first = range.StartRow + 1;
            var last = range.EndRow + 1;

            if (first < 1 || last > document.LineCount || last < first)
            {
                continue;
            }

            foldings.Add(new NewFolding(
                document.GetLineByNumber(first).Offset,
                document.GetLineByNumber(last).EndOffset)
            {
                // Closed on arrival - a fold that opens expanded has hidden nothing and saved no
                // scrolling, which is the entire point of computing it.
                DefaultClosed = true,
                Name = range.Length == 1 ? " 1 unchanged line " : $" {range.Length} unchanged lines ",
            });
        }

        // UpdateFoldings wants them ordered by offset, which CollapsedRegions already guarantees by
        // walking the document once.
        _foldings.UpdateFoldings(foldings, -1);
    }

    private void ApplyGutterStyle()
    {
        var foreground = this.TryFindResource("TextSecondary", out var brush) && brush is IBrush found
            ? found
            : Brushes.Gray;

        _lineNumbers.SetTextStyle(
            new Typeface(Editor.FontFamily),
            Editor.FontSize,
            foreground);
    }

    /// <summary>
    /// Re-resolves palette-derived colours after a theme switch. The renderers look their brushes up
    /// per pass, so this only needs to restyle the gutter and force a repaint.
    /// </summary>
    internal void OnThemeChanged()
    {
        ApplyGutterStyle();
        Editor.TextArea.TextView.Redraw();
    }
}
