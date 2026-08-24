using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Diff.Infrastructure;

/// <summary>
/// Binds every Core port to its adapter. This is the single place Infrastructure types are named, so
/// the UI's composition root never has to know one exists.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the diff engine, the text normalizer, and file access.</summary>
    public static IServiceCollection AddFubarDiffInfrastructure(this IServiceCollection services)
    {
        // All stateless, so singletons: no per-comparison allocation and nothing to reset.
        services.AddSingleton<IDiffEngine, DiffPlexDiffEngine>();
        services.AddSingleton<IInlineDiffEngine, DiffPlexInlineDiffEngine>();
        services.AddSingleton<ILineNormalizer, TextLineNormalizer>();
        services.AddSingleton<ITextFileReader, TextFileReader>();
        services.AddSingleton<ITextFileWriter, TextFileWriter>();

        return services;
    }
}
