using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Builds the comparison-settings hierarchy a diff can offer to write into, and writes it.
/// </summary>
/// <remarks>
/// <para>One implementation, because a rule written from a snapshot failure and a rule written from an
/// environment comparison are the same rule, in the same file, at the same level. The environment
/// window grew this first; the run window needed it the moment a failing row could be opened, and a
/// second copy is how the two come to disagree about where a rule goes.</para>
/// <para>In the UI project rather than Application because <see cref="DiffSettingsContext"/> is a UI
/// type - the same reason <c>DiffResponseComparer</c> lives here (spec §10.2).</para>
/// </remarks>
public interface IComparisonSettingsContext
{
    /// <summary>
    /// Builds what the embedded diff view needs to show a comparison the way this request would be
    /// judged: the rules that apply at <paramref name="requestPath"/>, and a way to save an
    /// adjustment back to whichever level the user picks.
    /// </summary>
    /// <param name="workspace">The workspace whose hierarchy the rules resolve through.</param>
    /// <param name="requestPath">The request the rules are resolved FOR - the innermost level.</param>
    /// <param name="onSaved">Run after a successful write, so a caller can re-judge what it is
    /// showing. A saved rule changes what "the same" means, and a list that did not react would be
    /// showing verdicts from before the rule existed.</param>
    Task<DiffSettingsContext> BuildAsync(
        Workspace workspace, string requestPath, Func<Task>? onSaved = null);
}

/// <inheritdoc cref="IComparisonSettingsContext"/>
public sealed class ComparisonSettingsContextFactory : IComparisonSettingsContext
{
    private readonly IAppSettingsService _appSettings;
    private readonly IInheritanceResolver _inheritance;
    private readonly IRequestStore _requests;
    private readonly IFolderConfigStore _folders;
    private readonly StatusLogViewModel _statusLog;

    public ComparisonSettingsContextFactory(
        IAppSettingsService appSettings,
        IInheritanceResolver inheritance,
        IRequestStore requests,
        IFolderConfigStore folders,
        StatusLogViewModel statusLog)
    {
        _appSettings = appSettings;
        _inheritance = inheritance;
        _requests = requests;
        _folders = folders;
        _statusLog = statusLog;
    }

    public async Task<DiffSettingsContext> BuildAsync(
        Workspace workspace, string requestPath, Func<Task>? onSaved = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var layers = new List<ComparisonSettingsLayer>();

        var app = await _appSettings.LoadAsync();
        if (app.Comparison is { } global)
        {
            layers.Add(new ComparisonSettingsLayer(global, ComparisonScope.Global, "Global"));
        }

        var chain = await _inheritance.GetInheritanceChainAsync(workspace.RootPath, requestPath);
        layers.AddRange(chain.ComparisonLayers);

        var request = await _requests.LoadRequestAsync(requestPath);

        return new DiffSettingsContext(
            layers,
            request.Comparison?.Clone(),
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(requestPath))),
            async (scope, settings) =>
            {
                if (await SaveAsync(requestPath, scope, settings) && onSaved is not null)
                {
                    await onSaved();
                }
            });
    }

    /// <summary>
    /// Writes one level's overrides. Fails SOFT: losing a comparison over a failed write is the wrong
    /// trade in a window whose job is the comparison.
    /// </summary>
    private async Task<bool> SaveAsync(string requestPath, ComparisonScope scope, ComparisonSettings? settings)
    {
        try
        {
            switch (scope)
            {
                case ComparisonScope.Global:
                    var app = await _appSettings.LoadAsync();
                    app.Comparison = settings;
                    await _appSettings.SaveAsync(app);
                    _statusLog.Log("Saved global comparison defaults");
                    break;

                case ComparisonScope.Folder when Path.GetDirectoryName(Path.GetDirectoryName(requestPath)) is { } folder:
                    var config = await _folders.LoadFolderConfigAsync(folder);
                    config.Comparison = settings;
                    await _folders.SaveFolderConfigAsync(folder, config);
                    _statusLog.Log($"Saved comparison settings to folder {Path.GetFileName(folder)}");
                    break;

                case ComparisonScope.Request:
                    var persisted = await _requests.LoadRequestAsync(requestPath);
                    persisted.Comparison = settings;
                    persisted.ResponseDiffIgnorePaths = [];
                    await _requests.SaveRequestAsync(requestPath, persisted);
                    _statusLog.Log($"Saved comparison settings to {Path.GetFileName(requestPath)}");
                    break;
            }

            return true;
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not save comparison settings: {ex.Message}");
            return false;
        }
    }
}
