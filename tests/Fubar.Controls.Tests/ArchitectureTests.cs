using System.Linq;
using Fubar.Controls;

namespace Fubar.Controls.Tests;

/// <summary>
/// Guards the library's core contract: Fubar.Controls is app-agnostic, so it drops into ANY Avalonia
/// app. Both apps in this repository consume it, so naming one as the "forbidden" dependency would be
/// arbitrary - this is an allowlist instead: the only things it may reference are Avalonia, the
/// AvaloniaEdit stack behind <see cref="JsonEditor"/>, and the BCL. Anything else - a host app, an MVVM
/// toolkit, a JSON or HTTP library - is a dependency consumers did not ask for, and breaks the promise
/// that the library drops into anything.
///
/// This matters MORE in a monorepo, not less: with everything a project reference away, adding
/// `using Fubar.Studio.Core` here compiles fine and nothing but this test would object.
/// </summary>
public class ArchitectureTests
{
    /// <summary>Assembly-name prefixes the library is allowed to depend on.</summary>
    private static readonly string[] AllowedPrefixes =
    [
        "Avalonia",       // Avalonia.Base / .Controls / .Markup.Xaml
        "AvaloniaEdit",   // the code editor behind JsonEditor
        "TextMateSharp",  // AvaloniaEdit's syntax highlighting, pulled in transitively
        "System",         // BCL
        "netstandard",
        "mscorlib",
    ];

    [Fact]
    public void ControlsLibrary_ReferencesOnly_AvaloniaAndTheBcl()
    {
        var unexpected = typeof(Badge).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !AllowedPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            $"Fubar.Controls must stay app-agnostic, but it references: {string.Join(", ", unexpected)}. "
            + "If the dependency is genuinely generic and belongs in the library, add its prefix to "
            + $"{nameof(AllowedPrefixes)} and say why in the PR.");
    }

    /// <summary>
    /// A control that knows about a domain concept isn't reusable. Names are a cheap, honest proxy:
    /// nothing in the public surface should be about requests, workspaces, environments, and so on.
    /// </summary>
    [Theory]
    [InlineData("Request")]
    [InlineData("Response")]
    [InlineData("Workspace")]
    [InlineData("Environment")]
    [InlineData("Auth")]
    [InlineData("OAuth")]
    [InlineData("Diff")]
    public void PublicTypes_DoNotLeak_DomainConcepts(string domainWord)
    {
        var offenders = typeof(Badge).Assembly
            .GetExportedTypes()
            .Select(t => t.Name)
            .Where(n => n.Contains(domainWord, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Public type name(s) reference the domain concept '{domainWord}': {string.Join(", ", offenders)}. "
            + "App-specific components belong in the consuming app, not in Fubar.Controls.");
    }
}
