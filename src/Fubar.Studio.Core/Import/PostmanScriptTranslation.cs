using System.Text.RegularExpressions;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>What a Postman test script became, and what it did not.</summary>
/// <param name="Assertions">Checks that translated directly.</param>
/// <param name="Captures">Variable writes that translated directly.</param>
/// <param name="Untranslated">Lines carrying logic this cannot express, verbatim.</param>
public sealed record ScriptTranslation(
    IReadOnlyList<Assertion> Assertions,
    IReadOnlyList<CaptureRule> Captures,
    IReadOnlyList<string> Untranslated)
{
    public bool AnythingTranslated => Assertions.Count > 0 || Captures.Count > 0;

    public static ScriptTranslation Empty { get; } = new([], [], []);
}

/// <summary>
/// Translates the common shapes of a Postman <c>test</c> script into this app's declarative assertions
/// and captures.
///
/// <para>The importer never read the <c>event</c> array at all, so every pre-request and test script a
/// team had written was dropped in silence - and the assertions they wrote are the thing they most
/// want to keep when leaving Postman. Two of its only warnings were about GraphQL and file bodies;
/// nothing was said about the scripts.</para>
///
/// <para><b>Deliberately not a JavaScript interpreter.</b> Postman scripts are arbitrary JS and this
/// app's tests are declarative, so a complete translation is not on the table. It recognises the
/// handful of shapes that make up most real scripts, and reports every line it could not place -
/// verbatim, so the user can decide. An honest refusal is worth more than a guess: silently producing
/// an assertion that does not mean what the original did is worse than producing none.</para>
/// </summary>
public static partial class PostmanScriptTranslation
{
    public static ScriptTranslation Translate(IEnumerable<string>? lines)
    {
        var assertions = new List<Assertion>();
        var captures = new List<CaptureRule>();
        var untranslated = new List<string>();

        foreach (var raw in lines ?? [])
        {
            var line = raw.Trim();

            if (IsIgnorable(line))
            {
                continue;
            }

            if (TryStatus(line, assertions)
                || TryResponseTime(line, assertions)
                || TryBodyContains(line, assertions)
                || TryJsonEquals(line, assertions)
                || TrySetVariable(line, captures))
            {
                continue;
            }

            untranslated.Add(line);
        }

        return new ScriptTranslation(assertions, captures, untranslated);
    }

    /// <summary>
    /// Structural noise rather than logic: blank lines, comments, and the <c>pm.test("...", () => {</c>
    /// wrapper with its closing brace. Reporting these as untranslated would bury the lines that
    /// actually carry meaning under boilerplate.
    /// </summary>
    private static bool IsIgnorable(string line) =>
        line.Length == 0
        || line.StartsWith("//", StringComparison.Ordinal)
        || line.StartsWith("/*", StringComparison.Ordinal)
        || line.StartsWith('*')
        || line is "}" or "});" or "})" or "{"
        || TestWrapperRegex().IsMatch(line);

    // pm.response.to.have.status(200)  |  pm.expect(pm.response.code).to.eql(200)
    private static bool TryStatus(string line, List<Assertion> into)
    {
        if (StatusRegex().Match(line) is not { Success: true } match)
        {
            return false;
        }

        into.Add(new Assertion
        {
            Source = ResponseField.StatusCode,
            Operator = AssertionOperator.Equals,
            Expected = match.Groups["code"].Value,
        });

        return true;
    }

    // pm.expect(pm.response.responseTime).to.be.below(500)
    private static bool TryResponseTime(string line, List<Assertion> into)
    {
        if (ResponseTimeRegex().Match(line) is not { Success: true } match)
        {
            return false;
        }

        into.Add(new Assertion
        {
            Source = ResponseField.ResponseTimeMs,
            Operator = AssertionOperator.LessThan,
            Expected = match.Groups["ms"].Value,
        });

        return true;
    }

    // pm.expect(pm.response.text()).to.include("ok")
    private static bool TryBodyContains(string line, List<Assertion> into)
    {
        if (BodyIncludesRegex().Match(line) is not { Success: true } match)
        {
            return false;
        }

        into.Add(new Assertion
        {
            Source = ResponseField.JsonBody,
            Target = "$",
            Operator = AssertionOperator.Contains,
            Expected = match.Groups["text"].Value,
        });

        return true;
    }

    // pm.expect(pm.response.json().data.id).to.eql("42")
    private static bool TryJsonEquals(string line, List<Assertion> into)
    {
        if (JsonEqualsRegex().Match(line) is not { Success: true } match)
        {
            return false;
        }

        into.Add(new Assertion
        {
            Source = ResponseField.JsonBody,
            Target = "$" + match.Groups["path"].Value,
            Operator = AssertionOperator.Equals,
            Expected = match.Groups["value"].Value.Trim('"', '\''),
        });

        return true;
    }

    // pm.environment.set("token", pm.response.json().access_token)
    private static bool TrySetVariable(string line, List<CaptureRule> into)
    {
        if (SetVariableRegex().Match(line) is not { Success: true } match)
        {
            return false;
        }

        into.Add(new CaptureRule
        {
            VariableName = match.Groups["name"].Value,
            Source = ResponseField.JsonBody,
            Expression = "$" + match.Groups["path"].Value,

            // SESSION, not Environment, whatever Postman called it. pm.environment.set is overwhelmingly
            // used for a token, and Environment scope in this app writes to a file that gets committed -
            // which is exactly the leak the rest of this codebase is built to avoid. A user who wants it
            // persisted can say so; one who does not, cannot un-commit it.
            Scope = CaptureScope.Session,
        });

        return true;
    }

    [GeneratedRegex(@"^\s*pm\.test\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex TestWrapperRegex();

    [GeneratedRegex(
        @"(pm\.response\.to\.have\.status\s*\(\s*(?<code>\d{3})|pm\.expect\s*\(\s*pm\.response\.code\s*\)\s*\.to\.(?:eql|equal|be\.eql)\s*\(\s*(?<code>\d{3}))",
        RegexOptions.CultureInvariant)]
    private static partial Regex StatusRegex();

    [GeneratedRegex(
        @"pm\.response\.responseTime\s*\)\s*\.to\.be\.below\s*\(\s*(?<ms>\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ResponseTimeRegex();

    [GeneratedRegex(
        @"pm\.response\.text\s*\(\s*\)\s*\)\s*\.to\.(?:include|contain)\s*\(\s*[""'](?<text>[^""']*)[""']",
        RegexOptions.CultureInvariant)]
    private static partial Regex BodyIncludesRegex();

    [GeneratedRegex(
        @"pm\.response\.json\s*\(\s*\)(?<path>(?:\.[A-Za-z_$][A-Za-z0-9_$]*|\[\d+\])+)\s*\)\s*\.to\.(?:eql|equal)\s*\(\s*(?<value>[^)]+?)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex JsonEqualsRegex();

    [GeneratedRegex(
        @"pm\.(?:environment|collectionVariables|globals)\.set\s*\(\s*[""'](?<name>[^""']+)[""']\s*,\s*pm\.response\.json\s*\(\s*\)(?<path>(?:\.[A-Za-z_$][A-Za-z0-9_$]*|\[\d+\])+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SetVariableRegex();
}
