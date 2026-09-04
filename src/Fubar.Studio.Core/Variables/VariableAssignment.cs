namespace Fubar.Studio.Core.Variables;

/// <summary>
/// Parsing for the <c>KEY=VALUE</c> form shared by <c>--var</c> and <c>--env-file</c>.
///
/// <para>Domain policy rather than an adapter detail, so the CLI can validate a flag without reaching
/// into Infrastructure - and so the rules below are testable on their own. They are the fiddly half of
/// the feature: people paste these straight out of a shell script, and a base64 secret ends in
/// <c>=</c>.</para>
/// </summary>
public static class VariableAssignment
{
    /// <summary>
    /// Splits one assignment, or returns nulls when the line carries none.
    ///
    /// <para>Blank lines and <c>#</c> comments carry none. A leading <c>export</c> is tolerated. One
    /// layer of surrounding quotes is stripped. Everything after the FIRST <c>=</c> is the value, so a
    /// base64 secret survives intact.</para>
    /// </summary>
    public static (string? Key, string? Value) Parse(string? line)
    {
        var text = (line ?? "").Trim();

        if (text.Length == 0 || text.StartsWith('#'))
        {
            return (null, null);
        }

        if (text.StartsWith("export ", StringComparison.Ordinal))
        {
            text = text["export ".Length..].TrimStart();
        }

        var separator = text.IndexOf('=');
        if (separator <= 0)
        {
            return (null, null);
        }

        var key = text[..separator].Trim();
        var value = text[(separator + 1)..].Trim();

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }

        return key.Length == 0 ? (null, null) : (key, value);
    }

    /// <summary>
    /// Why this <c>--var</c> cannot be used, or null when it can.
    ///
    /// <para>Refused rather than skipped: a malformed <c>--var</c> is a credential the caller believes
    /// they passed, and running without it produces a 401 that points at the API instead of at the
    /// typo.</para>
    /// </summary>
    public static string? DescribeInvalid(string text) =>
        Parse(text).Key is null ? $"\"{text}\" is not KEY=VALUE." : null;

    /// <summary>
    /// The lookup form of a name: upper-case is irrelevant and anything that is not a letter or digit
    /// becomes an underscore, so <c>{{api_key}}</c>, <c>API_KEY</c> and <c>api-key</c> are one variable.
    /// Some CI systems allow no other shape for an environment variable, and making the user care about
    /// the difference would be a bug report a week.
    /// </summary>
    public static string Normalise(string key) =>
        string.Concat((key ?? "").Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
