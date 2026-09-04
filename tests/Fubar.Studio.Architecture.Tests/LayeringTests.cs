using System.Reflection;
using NetArchTest.Rules;

namespace Fubar.Studio.Architecture.Tests;

/// <summary>
/// Guards the clean-architecture dependency direction: Presentation → Application → Core, Infrastructure
/// → Core, and the shared UI component library isolated from all app layers. If someone adds a reference
/// that points the wrong way, one of these fails.
/// </summary>
public class LayeringTests
{
    private const string Core = "Fubar.Studio.Core";
    private const string Application = "Fubar.Studio.Application";
    private const string Infrastructure = "Fubar.Studio.Infrastructure";
    private const string Ui = "Fubar.Studio.UI";
    private const string Controls = "Fubar.Controls";

    private static readonly Assembly CoreAsm = typeof(Fubar.Studio.Core.Models.RequestModel).Assembly;
    private static readonly Assembly ApplicationAsm = typeof(Fubar.Studio.Application.Requests.RequestExecutionService).Assembly;
    private static readonly Assembly InfrastructureAsm = typeof(Fubar.Studio.Infrastructure.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly UiAsm = typeof(Fubar.Studio.UI.ViewModels.MainViewModel).Assembly;

    private static void AssertNoDependency(Assembly assembly, string subject, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).Should().NotHaveDependencyOnAny(forbidden).GetResult();
        var offenders = result.FailingTypeNames is { } names ? string.Join(", ", names) : "";
        Assert.True(result.IsSuccessful, $"{subject} must not depend on [{string.Join(", ", forbidden)}]. Offenders: {offenders}");
    }

    [Fact]
    public void Core_depends_on_no_other_layer() =>
        AssertNoDependency(CoreAsm, "Core", Application, Infrastructure, Ui, Controls);

    [Fact]
    public void Application_depends_only_on_core() =>
        AssertNoDependency(ApplicationAsm, "Application", Infrastructure, Ui, Controls);

    [Fact]
    public void Infrastructure_depends_only_on_core() =>
        AssertNoDependency(InfrastructureAsm, "Infrastructure", Application, Ui, Controls);

    // NOTE: "Fubar.Controls does not depend on the app" is not asserted here, but it IS asserted -
    // by the allowlist in Fubar.Controls.Tests.ArchitectureTests, which is the stronger form: it
    // permits only Avalonia, AvaloniaEdit and the BCL, so it catches a dependency on ANY app rather
    // than on this one specifically. The assertions above carry the half that belongs on this side:
    // no app layer may depend on the UI control library.

    /// <summary>
    /// API Studio must not carry the C# compiler.
    ///
    /// <para>It did: <c>Composition.cs</c> called <c>AddFubarDiffInfrastructure()</c> to obtain a JSON
    /// differ, and that assembly referenced Roslyn for the structural comparison - so
    /// Microsoft.CodeAnalysis.CSharp (7.1 MB) and Microsoft.CodeAnalysis (3.1 MB) shipped inside an
    /// application whose own composition root explains, correctly, that it never compares source
    /// files. Roughly twelve times the size of its own assembly, for a port it never resolves.</para>
    ///
    /// <para>Asserted against the build OUTPUT, not against the managed reference graph, and that
    /// distinction is the whole test. The first version of this walked
    /// <c>GetReferencedAssemblies()</c> and passed happily with the bad project reference restored:
    /// the C# compiler ELIDES a reference to an assembly whose types are never used, so the manifest
    /// never mentions Roslyn - while MSBuild copies the transitive closure to the output anyway. The
    /// reference graph says what was compiled against; only the directory says what ships.</para>
    /// </summary>
    [Fact]
    public void No_compiler_ships_with_api_studio()
    {
        // The test's own output directory holds the same transitive closure, by the same copy rules,
        // because this project references Fubar.Studio.UI.
        var outputDirectory = Path.GetDirectoryName(UiAsm.Location)!;

        var offenders = Directory
            .EnumerateFiles(outputDirectory, "Microsoft.CodeAnalysis*.dll")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Fubar.Studio.UI must not ship the C# compiler - it compares HTTP responses, never source "
            + $"files, and this costs ~10 MB in the binary. Found: {string.Join(", ", offenders)}. "
            + "The Roslyn adapter belongs in Fubar.Diff.Infrastructure.Code, which only Fubar Diff "
            + "references; splitting the DI registration alone does NOT remove it, because a project "
            + "reference is what puts an assembly in the output.");
    }

    [Fact]
    public void Ui_view_models_do_not_depend_on_infrastructure()
    {
        // The composition root (Fubar.Studio.UI.Composition) is the one allowed UI→Infrastructure edge;
        // the ViewModels namespace must stay clean.
        var result = Types.InAssembly(UiAsm)
            .That().ResideInNamespace($"{Ui}.ViewModels")
            .ShouldNot().HaveDependencyOn(Infrastructure)
            .GetResult();

        var offenders = result.FailingTypeNames is { } names ? string.Join(", ", names) : "";
        Assert.True(result.IsSuccessful, $"UI ViewModels must not depend on Infrastructure. Offenders: {offenders}");
    }
}
