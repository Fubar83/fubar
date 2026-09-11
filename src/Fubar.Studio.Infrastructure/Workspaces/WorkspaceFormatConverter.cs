using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Workspaces;

/// <inheritdoc cref="IWorkspaceFormatConverter"/>
public sealed class WorkspaceFormatConverter : IWorkspaceFormatConverter
{
    private const string CollectionsDirName = "collections";
    private const string FolderConfigFileName = "_folder.json";
    private const string SnapshotDirectorySuffix = ".snapshots";
    private const string BackupDirName = ".fubar/backup";

    private readonly IWorkspaceStore _workspaces;

    public WorkspaceFormatConverter(IWorkspaceStore workspaces)
    {
        _workspaces = workspaces;
    }

    public ConversionPlan Preview(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var blockers = new List<string>();
        var warnings = new List<string>();
        var steps = new List<ConversionStep>();

        if (workspace.Manifest.Format == WorkspaceFormat.Endpoints)
        {
            blockers.Add("This workspace is already in the endpoints format.");
            return new ConversionPlan([], warnings, blockers);
        }

        var collections = Path.Combine(workspace.RootPath, CollectionsDirName);
        if (!Directory.Exists(collections))
        {
            blockers.Add("This workspace has no collections/ directory to convert.");
            return new ConversionPlan([], warnings, blockers);
        }

        foreach (var file in Requests(collections))
        {
            var directory = Path.GetDirectoryName(file)!;
            var name = Path.GetFileNameWithoutExtension(file);
            var endpointDirectory = Path.Combine(directory, name);

            // Refused rather than renamed around. "Get order.json" next to a folder called "Get order"
            // is ambiguous about what the user meant, and guessing produces a tree they did not write.
            if (Directory.Exists(endpointDirectory))
            {
                blockers.Add(
                    $"\"{Relative(workspace.RootPath, file)}\" cannot become an endpoint: a folder of that name is already there.");
                continue;
            }

            steps.Add(new ConversionStep(
                file,
                endpointDirectory,
                CaseMerge.ImplicitCaseName,
                Directory.Exists(Path.Combine(directory, name + SnapshotDirectorySuffix))));
        }

        if (steps.Count == 0 && blockers.Count == 0)
        {
            blockers.Add("There are no requests to convert.");
        }

        return new ConversionPlan(steps, warnings, blockers);
    }

    public async Task<ConversionResult> ConvertAsync(
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var plan = Preview(workspace);
        if (!plan.CanRun)
        {
            throw new InvalidOperationException(
                plan.Blockers.Count > 0 ? plan.Blockers[0] : "There is nothing to convert.");
        }

        // Copied before anything moves, and under .fubar/, which is already git-ignored - so the
        // safety net does not itself show up as a thousand added files in the diff being reviewed.
        var backup = Path.Combine(
            workspace.RootPath,
            BackupDirName,
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));

        CopyDirectory(Path.Combine(workspace.RootPath, CollectionsDirName), Path.Combine(backup, CollectionsDirName));

        var warnings = new List<string>(plan.Warnings);
        var converted = 0;

        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ConvertOneAsync(step, cancellationToken).ConfigureAwait(false);
                converted++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // One unreadable file does not abandon the other nineteen, and the backup is already
                // taken - so the honest thing is to say which one did not convert and carry on.
                warnings.Add($"\"{Path.GetFileName(step.RequestPath)}\" was left as it is: {ex.Message}");
            }
        }

        // Stamped LAST. An interrupted conversion then leaves a workspace that still opens in the
        // format it was in, with some endpoints already split beside their requests - visible, and
        // fixable by running this again.
        if (converted > 0)
        {
            workspace.Manifest.Format = WorkspaceFormat.Endpoints;
            await _workspaces
                .SaveAppManifestAsync(workspace.RootPath, workspace.Manifest, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ConversionResult(converted, backup, warnings);
    }

    /// <summary>
    /// Splits one request on the boundary between the OPERATION and the INVOCATION.
    /// </summary>
    /// <remarks>
    /// Method, URL, headers and auth are true every time it is called and stay on the endpoint. Query
    /// parameters, body, assertions and captures describe one call and become the single case - which
    /// is what makes the second case someone adds a change to that file rather than a copy of
    /// everything.
    /// </remarks>
    private static async Task ConvertOneAsync(ConversionStep step, CancellationToken cancellationToken)
    {
        RequestModel request;
        await using (var stream = File.OpenRead(step.RequestPath))
        {
            request = await JsonSerializer
                .DeserializeAsync<RequestModel>(stream, FubarJson.Options, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new JsonException("the file did not deserialize to a request");
        }

        Directory.CreateDirectory(step.EndpointDirectory);

        var endpoint = new RequestModel
        {
            Id = request.Id,
            Name = request.Name,
            Kind = request.Kind,
            Method = request.Method,
            Url = request.Url,
            Headers = request.Headers,
            Auth = request.Auth,
            AuthProfileId = request.AuthProfileId,
            TimeoutSeconds = request.TimeoutSeconds,
            SuppressedInheritedHeaderKeys = request.SuppressedInheritedHeaderKeys,
            Comparison = request.Comparison,
            Snapshot = request.Snapshot,
            Tolerances = request.Tolerances,
            Settings = request.Settings,
        };

        var endpointCase = new EndpointCase
        {
            Name = step.CaseName,
            QueryParams = request.QueryParams,
            Body = request.Body,
            Assertions = request.Assertions,
            Captures = request.Captures,
        };

        await JsonFile
            .WriteAtomicAsync(
                Path.Combine(step.EndpointDirectory, IEndpointStore.EndpointFileName),
                endpoint,
                FubarJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        await JsonFile
            .WriteAtomicAsync(
                Path.Combine(
                    step.EndpointDirectory,
                    IBatchStore.BatchesDirName,
                    CaseMerge.ImplicitCaseName,
                    "request-1.json"),
                endpointCase,
                FubarJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        // Snapshots move with the request, into the case's own directory. Left behind they would be
        // orphaned beside a file that no longer exists, and the first run after converting would
        // report "no snapshot" for a workspace that had them all along.
        if (step.MovesSnapshots)
        {
            var from = Path.Combine(
                Path.GetDirectoryName(step.RequestPath)!,
                Path.GetFileNameWithoutExtension(step.RequestPath) + SnapshotDirectorySuffix);

            var to = Path.Combine(
                step.EndpointDirectory, IEndpointStore.SnapshotsDirName, "request-1");

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
        }

        File.Delete(step.RequestPath);
    }

    /// <summary>
    /// Every file that is actually a request.
    /// </summary>
    /// <remarks>
    /// Not every <c>.json</c> under <c>collections/</c> is one, and treating one of the others as a
    /// request would be the worst outcome here: a recorded snapshot turned into an endpoint that
    /// sends nothing, with the real snapshot deleted underneath it. Excluded are <c>_folder.json</c>,
    /// anything inside a <c>*.snapshots/</c> directory, and anything already belonging to an endpoint -
    /// the second matters because this runs over workspaces that already have snapshots, and the
    /// third because a conversion interrupted half way must be safe to run again.
    /// </remarks>
    private static IEnumerable<string> Requests(string collections) =>
        Directory
            .EnumerateFiles(collections, "*.json", SearchOption.AllDirectories)
            .Where(IsRequest)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsRequest(string file)
    {
        var name = Path.GetFileName(file);

        if (string.Equals(name, FolderConfigFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, IEndpointStore.EndpointFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var segment = Path.GetFileName(directory);

            // Everything an endpoint OWNS is already converted, including the items inside a batch -
            // which are request-shaped files and would otherwise be converted again, into endpoints
            // nested inside a batch.
            if (segment.EndsWith(SnapshotDirectorySuffix, StringComparison.OrdinalIgnoreCase)
                || IEndpointStore.IsReservedEndpointChild(segment))
            {
                return false;
            }

            if (string.Equals(segment, CollectionsDirName, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return true;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
