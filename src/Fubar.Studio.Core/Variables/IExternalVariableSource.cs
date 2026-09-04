namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Variable values supplied from OUTSIDE the workspace - the process environment, <c>--var</c> flags,
/// an <c>--env-file</c>. Consulted before the environment file and before the OS keyring.
///
/// <para>This exists because the documented CI story could not authenticate. A secret variable resolves
/// through <c>ISecretStoreService</c>, which is the host OS keyring; a build agent has no keyring, so
/// the lookup failed, the resolver left the token as literal text, and the pipeline sent
/// <c>Authorization: Bearer {{api_key}}</c> to a real endpoint and reported a plain 401. The only
/// workaround available to a user was to mark the variable Normal and commit its value - so the missing
/// feature was also the thing generating the insecure workaround.</para>
///
/// <para><b>An input, never storage.</b> Nothing writes back here, and no value from here is ever
/// logged or written to an environment file: it came from the caller's secret manager, and putting it
/// on disk is the failure this whole area exists to prevent.</para>
/// </summary>
public interface IExternalVariableSource
{
    /// <summary>The value for <paramref name="key"/>, or null when this source does not supply it.</summary>
    string? TryGet(string key);

    /// <summary>Every key this source supplies, so variable listings and autocomplete can show them
    /// alongside the environment's own. Values are deliberately not exposed here.</summary>
    IReadOnlyCollection<string> Keys { get; }
}
