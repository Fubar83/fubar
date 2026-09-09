using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// Everything <see cref="RequestEditorViewModel"/> needs from the rest of the application, in one
/// object.
///
/// <para>It took twenty-four constructor parameters. That is not merely ugly: it made adding a
/// dependency a five-place edit - the view model, the factory, and every test that built one - which
/// is a large part of why the editor was the least-tested type in the application while being the one
/// every feature runs through.</para>
///
/// <para>Registered as a singleton and resolved as a unit, so a new dependency is one line here and
/// nothing anywhere else. This is a decomposition of the CONSTRUCTOR, not of the class - the tab view
/// models it composes are already the right split, and pretending otherwise would be a rewrite
/// disguised as a cleanup.</para>
/// </summary>
/// <param name="RequestStore">Loads and saves the request document.</param>
/// <param name="AuthProfileStore">The workspace's reusable auth profiles.</param>
/// <param name="InheritanceResolver">Folder-cascaded headers and auth.</param>
/// <param name="RequestExecution">The send pipeline.</param>
/// <param name="HistoryService">Execution history for the History tab.</param>
/// <param name="CurlExport">Copy as curl.</param>
/// <param name="SchemaValidator">Body schema validation.</param>
/// <param name="JsonPathEvaluator">The response pane's JSONPath filter.</param>
/// <param name="VariableResolver">Resolves and lists <c>{{variables}}</c>.</param>
/// <param name="AuthProvider">The auth prestep, and the Auth tab's Test button.</param>
/// <param name="Discovery">OpenID discovery for the token-request editor.</param>
/// <param name="SignIn">The authorization-code sign-in flow.</param>
/// <param name="Clipboard">Copy actions.</param>
/// <param name="FilePicker">Body file selection and response saving.</param>
/// <param name="StatusLog">Where this editor reports.</param>
/// <param name="DiffPreview">Response comparison.</param>
/// <param name="ResponseBaseline">The pinned response, shared across editors.</param>
/// <param name="AppSettings">Global comparison defaults.</param>
/// <param name="FolderConfigStore">Folder-level comparison settings.</param>
/// <param name="ComparisonSettingsContext">Builds the settings hierarchy a diff can write into, and
/// writes it - shared with the run window, so a rule saved from a snapshot failure and one saved from
/// an environment comparison land in the same place.</param>
public sealed record RequestEditorServices(
    IRequestStore RequestStore,
    IAuthProfileStore AuthProfileStore,
    IInheritanceResolver InheritanceResolver,
    IRequestExecutionService RequestExecution,
    IHistoryService HistoryService,
    ICurlExportService CurlExport,
    IJsonSchemaValidator SchemaValidator,
    IJsonPathEvaluator JsonPathEvaluator,
    IVariableResolver VariableResolver,
    IAuthProvider AuthProvider,
    IOpenIdDiscoveryService Discovery,
    SignInService SignIn,
    IClipboardService Clipboard,
    IFilePickerService FilePicker,
    StatusLogViewModel StatusLog,
    IDiffPreviewService DiffPreview,
    IResponseBaselineService ResponseBaseline,
    IAppSettingsService AppSettings,
    IFolderConfigStore FolderConfigStore,
    IComparisonSettingsContext ComparisonSettingsContext,
    Fubar.Studio.Application.Comparison.IRequestComparisonSettings ComparisonSettings);
