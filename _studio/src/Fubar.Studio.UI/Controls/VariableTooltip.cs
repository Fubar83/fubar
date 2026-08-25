using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.UI.Controls;

/// <summary>
/// What <see cref="VariableTooltip"/> needs to resolve <c>{{key}}</c> tokens for whichever TextBox
/// it's attached to.
/// </summary>
public sealed record VariableTooltipContext(IVariableResolver Resolver, Workspace Workspace, WorkspaceEnvironment? ActiveEnvironment, bool SecretsRevealed);

/// <summary>
/// Attached-property behavior implementing the Universal Variable Tooltip system
/// (RequestEditorPane.md §4) on any <see cref="TextBox"/>: as its <c>Text</c> changes, tokenizes
/// every <c>{{name}}</c> occurrence and sets a resolved-value/undefined summary as the box's
/// hover tooltip, plus a <c>variable-undefined</c> or <c>variable-valid</c> style class (see
/// Fubar.Controls' <c>Palette.axaml</c> <c>VariableValidBrush</c>/<c>VariableUndefinedBrush</c> tokens)
/// so the box's border tints amber/blue. Attach via
/// <c>controls:VariableTooltip.Context="{Binding SomeVariableTooltipContext}"</c> on a TextBox.
///
/// <para><b>Scope note:</b> this does not recolor individual <c>{{token}}</c> substrings inline
/// within the box's own text run (the spec's per-token blue/amber pill styling) - that needs a
/// custom-rendered text presenter, a materially larger control than fits this pass. Instead the
/// whole box gets one accent border (amber if anything is undefined, blue if every token resolves,
/// neutral if there are no tokens) plus a tooltip listing each token's resolution - same
/// information, coarser presentation.</para>
/// </summary>
public static partial class VariableTooltip
{
    public static readonly AttachedProperty<VariableTooltipContext?> ContextProperty =
        AvaloniaProperty.RegisterAttached<TextBox, VariableTooltipContext?>("Context", typeof(VariableTooltip));

    public static void SetContext(TextBox element, VariableTooltipContext? value) => element.SetValue(ContextProperty, value);

    public static VariableTooltipContext? GetContext(TextBox element) => element.GetValue(ContextProperty);

    static VariableTooltip()
    {
        ContextProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            box.PropertyChanged -= OnTextBoxPropertyChanged;
            box.PropertyChanged += OnTextBoxPropertyChanged;
            Refresh(box);
        });
    }

    private static void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.Property == TextBox.TextProperty)
        {
            Refresh(box);
        }
    }

    private static void Refresh(TextBox box)
    {
        var context = GetContext(box);
        var text = box.Text ?? "";

        if (context is null || TokenRegex().Matches(text) is not { Count: > 0 } matches)
        {
            ToolTip.SetTip(box, null);
            SetClass(box, "variable-undefined", false);
            SetClass(box, "variable-valid", false);
            return;
        }

        var lines = new List<string>();
        var anyUndefined = false;

        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value;
            var resolution = context!.Resolver.Resolve(key, context.Workspace, context.ActiveEnvironment);

            if (resolution.IsDefined)
            {
                var display = !context.SecretsRevealed && LooksSecret(key) ? "••••••" : resolution.Value;
                lines.Add($"{{{{{key}}}}} = {display}  ({resolution.SourceName})");
            }
            else
            {
                anyUndefined = true;
                lines.Add($"{{{{{key}}}}}: undefined - not found in \"{context.ActiveEnvironment?.Name ?? "active environment"}\"");
            }
        }

        ToolTip.SetTip(box, string.Join("\n", lines));
        SetClass(box, "variable-undefined", anyUndefined);
        SetClass(box, "variable-valid", !anyUndefined);
    }

    // Best-effort mask for the tooltip preview only - IVariableResolver already knows the true
    // IsSecret flag, but doesn't currently surface it in VariableResolution; matching by name is a
    // reasonable stand-in until that's threaded through.
    private static bool LooksSecret(string key) => key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase);

    private static void SetClass(TextBox box, string className, bool value)
    {
        if (value)
        {
            if (!box.Classes.Contains(className))
            {
                box.Classes.Add(className);
            }
        }
        else
        {
            box.Classes.Remove(className);
        }
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenRegex();
}
