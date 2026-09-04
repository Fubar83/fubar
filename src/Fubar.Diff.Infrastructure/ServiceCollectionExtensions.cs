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
/// Binds Core ports to their adapters. This is the single place Infrastructure types are named, so a
/// composition root never has to know one exists.
///
/// <para>Split in two because the two consumers want different amounts. Fubar Diff compares files and
/// folders of source; API Studio compares two HTTP responses, and took the whole assembly to get a
/// JSON differ - which is how it came to ship the C# compiler. Whether the registration is one method
/// or two does not by itself remove an assembly from the output (a project REFERENCE is what puts it
/// there), so the Roslyn adapter also lives in its own project now; see
/// <c>Fubar.Diff.Infrastructure.Code</c>.</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The text and structured-document half: diffing, normalisation, and the JSON/YAML parsers.
    /// Everything needed to compare two strings, and nothing that touches a file system or a compiler.
    /// This is all API Studio wants.
    /// </summary>
    public static IServiceCollection AddFubarDiffTextAndJson(this IServiceCollection services)
    {
        // All stateless, so singletons: no per-comparison allocation and nothing to reset.
        services.AddSingleton<IDiffEngine, DiffPlexDiffEngine>();
        services.AddSingleton<IInlineDiffEngine, DiffPlexInlineDiffEngine>();
        services.AddSingleton<ILineNormalizer, TextLineNormalizer>();
        services.AddSingleton<IJsonParser, JsonAstParser>();
        services.AddSingleton<IYamlParser, YamlAstParser>();

        // The readers come with this half rather than the file half: FileComparisonService requires a
        // text reader in its constructor, so a host that can compare anything at all needs one.
        services.AddSingleton<ITextFileReader, TextFileReader>();
        services.AddSingleton<IBinaryFileReader, BinaryFileReader>();

        return services;
    }

    /// <summary>
    /// The rest: writing files, walking and copying folders, watching for changes, and the settings
    /// stores. Fubar Diff's own half - an API client has no folder tree to compare and nothing to copy.
    /// </summary>
    public static IServiceCollection AddFubarDiffFiles(this IServiceCollection services)
    {
        services.AddSingleton<IFileCopier, FileCopier>();
        services.AddSingleton<ITextFileWriter, TextFileWriter>();
        services.AddSingleton<IFolderScanner, FileSystemFolderScanner>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IProjectConfigStore, FileSystemProjectConfigStore>();

        // NOT a singleton, unlike everything above: a watcher owns OS handles and is bound to one
        // comparison's files, so each tab and each merge window needs its own to dispose when it closes.
        services.AddTransient<IFileChangeWatcher, FileSystemChangeWatcher>();

        return services;
    }

    /// <summary>Both halves - the shape Fubar Diff wants, and what this method used to be.</summary>
    public static IServiceCollection AddFubarDiffInfrastructure(this IServiceCollection services) =>
        services.AddFubarDiffTextAndJson().AddFubarDiffFiles();
}
