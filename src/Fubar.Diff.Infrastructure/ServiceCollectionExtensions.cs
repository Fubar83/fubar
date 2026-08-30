using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Files;
using Fubar.Diff.Infrastructure.Folders;
using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.Infrastructure.Settings;
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
        services.AddSingleton<IBinaryFileReader, BinaryFileReader>();
        services.AddSingleton<ITextFileWriter, TextFileWriter>();
        services.AddSingleton<IJsonParser, JsonAstParser>();
        services.AddSingleton<IFolderScanner, FileSystemFolderScanner>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        // NOT a singleton, unlike everything above: a watcher owns OS handles and is bound to one
        // comparison's files, so each tab and each merge window needs its own to dispose when it closes.
        services.AddTransient<IFileChangeWatcher, FileSystemChangeWatcher>();

        return services;
    }
}
