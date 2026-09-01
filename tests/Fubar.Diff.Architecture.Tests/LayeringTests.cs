using System.Reflection;
using NetArchTest.Rules;

namespace Fubar.Diff.Architecture.Tests;

/// <summary>
/// The boundary guard. Dependencies point inward only, and the diff algorithm stays behind its port -
/// if either rule breaks, the build fails here rather than being discovered a refactor later.
/// </summary>
public class LayeringTests
{
    private const string Core = "Fubar.Diff.Core";
    private const string Application = "Fubar.Diff.Application";
    private const string Infrastructure = "Fubar.Diff.Infrastructure";
    private const string Ui = "Fubar.Diff.UI";

    private static readonly Assembly CoreAsm = typeof(Fubar.Diff.Core.Models.DiffLine).Assembly;
    private static readonly Assembly ApplicationAsm = typeof(Fubar.Diff.Application.Comparison.FileComparisonService).Assembly;
    private static readonly Assembly InfrastructureAsm = typeof(Fubar.Diff.Infrastructure.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly UiAsm = typeof(Fubar.Diff.UI.ViewModels.ShellViewModel).Assembly;

    private static void AssertNoDependency(Assembly assembly, string subject, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).Should().NotHaveDependencyOnAny(forbidden).GetResult();
        var offenders = result.FailingTypeNames is { } names ? string.Join(", ", names) : "";
        Assert.True(result.IsSuccessful, $"{subject} must not depend on [{string.Join(", ", forbidden)}]. Offenders: {offenders}");
    }

    [Fact]
    public void Core_depends_on_no_other_layer() =>
        AssertNoDependency(CoreAsm, "Core", Application, Infrastructure, Ui);

    [Fact]
    public void Application_depends_only_on_core() =>
        AssertNoDependency(ApplicationAsm, "Application", Infrastructure, Ui);

    [Fact]
    public void Infrastructure_depends_only_on_core() =>
        AssertNoDependency(InfrastructureAsm, "Infrastructure", Application, Ui);

    /// <summary>
    /// Core is pure domain: no Avalonia, no file system, no diff library. This is what makes the
    /// comparison rules testable with nothing but objects, and what would have to be given up first
    /// if someone reached for a convenience type from the wrong layer.
    /// </summary>
    [Fact]
    public void Core_depends_on_no_third_party_framework() =>
        AssertNoDependency(
            CoreAsm, "Core", "Avalonia", "DiffPlex", "CommunityToolkit", "Microsoft.Extensions", "Microsoft.CodeAnalysis");

    /// <summary>
    /// DiffPlex is an implementation detail of one adapter. If it leaks anywhere else, swapping the
    /// algorithm stops being a one-file change - which is the entire reason IDiffEngine exists.
    /// </summary>
    [Theory]
    [InlineData(nameof(Core))]
    [InlineData(nameof(Application))]
    [InlineData(nameof(Ui))]
    public void DiffPlex_is_confined_to_infrastructure(string layer)
    {
        var assembly = layer switch
        {
            nameof(Core) => CoreAsm,
            nameof(Application) => ApplicationAsm,
            _ => UiAsm,
        };

        AssertNoDependency(assembly, layer, "DiffPlex");
    }

    /// <summary>
    /// Roslyn is held to exactly the same rule, and it matters more here than it does for DiffPlex.
    /// The C# parser is by far the largest dependency in the app, and it is one AMONG SEVERAL possible
    /// structure parsers - the point of <c>ICodeStructureParser</c> is that a second language arrives
    /// as one more adapter rather than as a change to the differ, the tree and the UI. A syntax type
    /// reaching Core or Application would end that quietly.
    /// </summary>
    [Theory]
    [InlineData(nameof(Core))]
    [InlineData(nameof(Application))]
    [InlineData(nameof(Ui))]
    public void Roslyn_is_confined_to_infrastructure(string layer)
    {
        var assembly = layer switch
        {
            nameof(Core) => CoreAsm,
            nameof(Application) => ApplicationAsm,
            _ => UiAsm,
        };

        AssertNoDependency(assembly, layer, "Microsoft.CodeAnalysis");
    }

    /// <summary>
    /// The composition root is the one allowed UI -> Infrastructure edge; view models must resolve
    /// everything through Core ports and Application services.
    /// </summary>
    [Fact]
    public void Ui_view_models_do_not_depend_on_infrastructure()
    {
        var result = Types.InAssembly(UiAsm)
            .That().ResideInNamespace($"{Ui}.ViewModels")
            .ShouldNot().HaveDependencyOn(Infrastructure)
            .GetResult();

        var offenders = result.FailingTypeNames is { } names ? string.Join(", ", names) : "";
        Assert.True(result.IsSuccessful, $"UI ViewModels must not depend on {Infrastructure}. Offenders: {offenders}");
    }
}
