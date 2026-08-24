using System.Linq;
using Fubar.Controls;

namespace Fubar.Controls.Tests;

/// <summary>
/// Guards the library's core contract: Fubar.Controls is app-agnostic. If a component ever pulls in the
/// domain (Fubar.Studio.Core) or an app assembly, it stops being reusable across apps - this fails the build's
/// test gate before that ships.
/// </summary>
public class ArchitectureTests
{
    [Theory]
    [InlineData("Fubar.Studio.Core")]
    [InlineData("Fubar.Studio.Infrastructure")]
    [InlineData("FubarAPI")]
    public void ControlsLibrary_DoesNotReference_DomainOrAppAssemblies(string forbidden)
    {
        var referenced = typeof(Badge).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name);

        Assert.DoesNotContain(forbidden, referenced);
    }
}
