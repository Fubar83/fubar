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

    /// <summary>
    /// The endpoint a path belongs to, or null.
    /// </summary>
    /// <remarks>
    /// Climbs out of the directories an endpoint OWNS and stops at anything else. An item is three
    /// levels down - <c>&lt;endpoint&gt;/batches/&lt;batch&gt;/&lt;item&gt;.json</c> - and a batch two,
    /// so this walks rather than stepping a fixed number of times. A plain folder is not owned by the
    /// endpoint above it, so it resolves to nothing rather than to its grandparent.
    /// </remarks>
    public string? EndpointDirectoryOf(string path)
    {
        var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        // Three, because that is as deep as an endpoint's own directories go.
        for (var i = 0; i <= 3 && current is { Length: > 0 }; i++)
        {
            if (IsEndpoint(current))
            {
                return current;
            }

            var parent = Path.GetDirectoryName(current);

            // Owned when this IS one of the reserved directories, or when it is a batch - which is a
            // directory sitting inside batches/ and named by its author.
            var owned = IEndpointStore.IsReservedEndpointChild(Path.GetFileName(current))
                || (parent is not null && string.Equals(
                    Path.GetFileName(parent), IBatchStore.BatchesDirName, StringComparison.OrdinalIgnoreCase));

            if (!owned)
            {
                return null;
            }

            current = parent;
        }

        return null;
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

    public string ProposeCasePath(string endpointDirectory, string caseName) =>
        Unique(Path.Combine(endpointDirectory, IEndpointStore.CasesDirName), caseName);

    public string RenameCase(string caseFilePath, string newName)
    {
        if (!DocumentName.IsValid(newName))
        {
            throw new ArgumentException($"\"{newName}\" cannot name a case.", nameof(newName));
        }

        var destination = Path.Combine(Path.GetDirectoryName(caseFilePath) ?? "", newName + Extension);

        // Compared ordinally so "created" -> "Created" is still done rather than skipped: the listing
        // is what a reader sees, and a case that will not take its own capitalisation looks broken.
        if (string.Equals(destination, caseFilePath, StringComparison.Ordinal))
        {
            return caseFilePath;
        }

        if (!string.Equals(destination, caseFilePath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(destination))
        {
            throw new IOException($"There is already a case called \"{newName}\".");
        }

        File.Move(caseFilePath, destination);

        return destination;
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
}
