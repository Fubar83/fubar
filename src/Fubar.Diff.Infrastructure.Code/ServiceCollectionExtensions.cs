using Fubar.Diff.Core.Code;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Diff.Infrastructure.Code;

/// <summary>
/// Binds <see cref="ICodeStructureParser"/> to the Roslyn adapter.
///
/// <para>Its own project, and therefore its own registration, so a host that never compares source
/// files does not reference the C# compiler - which is how API Studio came to ship 10.2 MB of Roslyn
/// to diff two JSON responses.</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFubarDiffCodeStructure(this IServiceCollection services) =>
        services.AddSingleton<ICodeStructureParser, RoslynCodeStructureParser>();
}
