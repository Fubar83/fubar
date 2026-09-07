using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.Infrastructure.Variables;

/// <inheritdoc cref="IExternalVariableSource"/>
/// <remarks>
/// Three inputs, highest precedence first: <c>--var K=V</c>, then <c>--env-file</c>, then
/// <c>FUBAR_VAR_*</c> from the process environment. A flag beats a file beats the ambient environment -
/// the order someone debugging a pipeline expects when they add a flag to override what the agent
/// already sets. The <c>KEY=VALUE</c> parsing itself is <see cref="VariableAssignment"/> in Core, so
/// the CLI can validate a flag without reaching into Infrastructure.
/// </remarks>
public sealed class ExternalVariableSource : IExternalVariableSource
{
    /// <summary>Prefix for process environment variables: <c>{{api_key}}</c> is fed by
    /// <c>FUBAR_VAR_API_KEY</c>.</summary>
    public const string EnvironmentPrefix = "FUBAR_VAR_";

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nothing supplied from outside - the GUI's case.</summary>
    public static ExternalVariableSource Empty { get; } = new();

    /// <summary>
    /// Copies another source's values into this one. The DI singleton is registered before the command
    /// line has been validated - an unreadable <c>--env-file</c> has to exit 2 with a message rather
    /// than throw out of composition - so the CLI builds a source once it knows the inputs are good and
    /// loads it into the registered instance.
    /// </summary>
    public void LoadFrom(ExternalVariableSource other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var pair in other._values)
        {
            _values[pair.Key] = pair.Value;
        }
    }

    public IReadOnlyCollection<string> Keys => _values.Keys;

    public string? TryGet(string key) =>
        key is not null && _values.TryGetValue(VariableAssignment.Normalise(key), out var value) ? value : null;

    /// <summary>
    /// Builds a source from the three inputs, applied lowest precedence first so later ones overwrite.
    /// </summary>
    /// <param name="environment">The process environment, or null to read the real one.</param>
    /// <param name="envFileLines">Lines of an <c>--env-file</c>, already read.</param>
    /// <param name="varFlags">Raw <c>KEY=VALUE</c> strings from <c>--var</c>.</param>
    public static ExternalVariableSource Build(
        IDictionary<string, string>? environment = null,
        IEnumerable<string>? envFileLines = null,
        IEnumerable<string>? varFlags = null)
    {
        var source = new ExternalVariableSource();

        foreach (var (key, value) in ReadProcessEnvironment(environment))
        {
            source._values[VariableAssignment.Normalise(key)] = value;
        }

        foreach (var line in (envFileLines ?? []).Concat(varFlags ?? []))
        {
            if (VariableAssignment.Parse(line) is { Key: { } key, Value: { } value })
            {
                source._values[VariableAssignment.Normalise(key)] = value;
            }
        }

        return source;
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadProcessEnvironment(IDictionary<string, string>? provided)
    {
        if (provided is not null)
        {
            foreach (var pair in provided.Where(p => p.Key.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new(pair.Key[EnvironmentPrefix.Length..], pair.Value);
            }

            yield break;
        }

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return new(key[EnvironmentPrefix.Length..], entry.Value as string ?? "");
            }
        }
    }
}
