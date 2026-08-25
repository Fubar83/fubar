using System.Collections.Generic;
using Fubar.Studio.Core.Import;

namespace Fubar.Studio.UI.Services;

/// <summary>The user's confirmed choices from the import diff dialog: the parsed plan, the request and
/// variable changes they ticked to apply, and the remaining options.</summary>
public sealed record ImportDialogResult(
    OpenApiImportPlan Plan,
    IReadOnlyList<RequestDiff> SelectedRequests,
    IReadOnlyList<VariableDiff> SelectedVariables,
    OpenApiImportOptions Options);

/// <summary>Shows the OpenAPI/Swagger import dialog (source input + parsed preview + options) and
/// returns the user's choice, or null if they cancelled. Abstracted so view models can request it
/// without depending on the view layer, matching the file/folder picker services.</summary>
public interface IImportDialogService
{
    Task<ImportDialogResult?> ShowAsync(string workspaceRoot);

    /// <summary>Shows the "paste a curl command" prompt; returns the pasted text, or null if cancelled.</summary>
    Task<string?> ShowCurlAsync();
}
