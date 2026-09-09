using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Workspaces;

/// <summary>
/// Endpoint directories and their cases on disk (spec §3.1).
/// </summary>
/// <remarks>
/// Separate from <see cref="WorkspaceService"/> rather than folded into it: that type is already the
/// whole workspace surface, and cases are the one part of it that only exists in one of the two
/// formats. A workspace in the requests format never calls any of this.
/// </remarks>
public sealed class FileEndpointStore : IEndpointStore
{
    private const string Extension = ".json";

    public bool IsEndpoint(string directory) =>
        File.Exists(Path.Combine(directory, IEndpointStore.EndpointFileName));

    public string? EndpointDirectoryOf(string path)
    {
        if (Directory.Exists(path))
        {
            return IsEndpoint(path) ? path : null;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is null)
        {
            return null;
        }

        // A case file is one level deeper, inside cases/ - and so is a batch of this endpoint's own,
        // inside batches/. Both are reserved names an endpoint owns rather than folders in the tree.
        var directoryName = Path.GetFileName(parent);

        if (string.Equals(directoryName, IEndpointStore.CasesDirName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(directoryName, IBatchStore.BatchesDirName, StringComparison.OrdinalIgnoreCase))
        {
            parent = Path.GetDirectoryName(parent);
        }

        return parent is not null && IsEndpoint(parent) ? parent : null;
    }

    public IReadOnlyList<CaseSummary> ListCases(string endpointDirectory)
    {
        var casesPath = Path.Combine(endpointDirectory, IEndpointStore.CasesDirName);
        if (!Directory.Exists(casesPath))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(casesPath, $"*{Extension}")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new CaseSummary(Path.GetFileNameWithoutExtension(f), f)),
        ];
    }

    public async Task<EndpointCase> LoadCaseAsync(string caseFilePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(caseFilePath);

        var loaded = await JsonSerializer
            .DeserializeAsync<EndpointCase>(stream, FubarJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return loaded ?? throw new InvalidDataException($"\"{caseFilePath}\" did not deserialize to a case.");
    }

    public Task SaveCaseAsync(
        string caseFilePath,
        EndpointCase endpointCase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpointCase);

        Directory.CreateDirectory(Path.GetDirectoryName(caseFilePath)!);
        return JsonFile.WriteAtomicAsync(caseFilePath, endpointCase, FubarJson.Options, cancellationToken);
    }

    public string CreateCase(string endpointDirectory, string caseName)
    {
        var casesPath = Path.Combine(endpointDirectory, IEndpointStore.CasesDirName);
        Directory.CreateDirectory(casesPath);

        var path = Unique(casesPath, caseName);

        var created = new EndpointCase { Name = Path.GetFileNameWithoutExtension(path) };
        File.WriteAllText(path, JsonSerializer.Serialize(created, FubarJson.Options));

        return path;
    }

    public string CreateEndpoint(string parentDirectory, string endpointName)
    {
        var directory = UniqueDirectory(parentDirectory, endpointName);
        Directory.CreateDirectory(directory);

        var endpoint = new RequestModel { Name = Path.GetFileName(directory) };
        File.WriteAllText(
            Path.Combine(directory, IEndpointStore.EndpointFileName),
            JsonSerializer.Serialize(endpoint, FubarJson.Options));

        return directory;
    }

    /// <summary>A free file name, so creating twice makes two cases rather than overwriting the
    /// first - the same rule the request store already follows.</summary>
    private static string Unique(string directory, string name)
    {
        var candidate = Path.Combine(directory, name + Extension);
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{name} {n++}{Extension}");
        }

        return candidate;
    }

    private static string UniqueDirectory(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        var n = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(parent, $"{name} {n++}");
        }

        return candidate;
    }
}
