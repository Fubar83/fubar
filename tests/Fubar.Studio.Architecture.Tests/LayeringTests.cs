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

    // NOTE: there is deliberately no "Controls does not depend on the app" test any more. Fubar.Controls
    // is an external package now, compiled in its own repository before this one is built, so such a
    // test could not fail here even if the rule were broken. That rule is enforced where it can be:
    // ArchitectureTests in github.com/Fubar83/fubar-components. The assertions above still carry the
    // half that matters on this side - no app layer may depend on the UI control library.

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
