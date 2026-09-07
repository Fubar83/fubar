using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Import;

/// <summary>
/// The Postman importer never read the event array, so every test script a team had written was
/// dropped in silence - and those assertions are the thing people most want to keep when leaving
/// Postman.
///
/// <para>Not a JavaScript interpreter, and these tests say so both ways: the shapes that make up most
/// real scripts translate, and everything else is REPORTED rather than guessed at. An assertion that
/// silently means something different from the original is worse than no assertion.</para>
/// </summary>
public class PostmanScriptTranslationTests
{
    private static ScriptTranslation Translate(params string[] lines) =>
        PostmanScriptTranslation.Translate(lines);

    [Theory]
    [InlineData("pm.response.to.have.status(200);", "200")]
    [InlineData("    pm.response.to.have.status(404)", "404")]
    [InlineData("pm.expect(pm.response.code).to.eql(201);", "201")]
    public void A_status_check_becomes_a_status_assertion(string line, string expected)
    {
        var assertion = Assert.Single(Translate(line).Assertions);

        Assert.Equal(ResponseField.StatusCode, assertion.Source);
        Assert.Equal(AssertionOperator.Equals, assertion.Operator);
        Assert.Equal(expected, assertion.Expected);
    }

    [Fact]
    public void A_response_time_check_becomes_a_less_than_assertion()
    {
        var assertion = Assert.Single(Translate("pm.expect(pm.response.responseTime).to.be.below(500);").Assertions);

        Assert.Equal(ResponseField.ResponseTimeMs, assertion.Source);
        Assert.Equal(AssertionOperator.LessThan, assertion.Operator);
        Assert.Equal("500", assertion.Expected);
    }

    [Fact]
    public void A_body_include_becomes_a_contains_assertion()
    {
        var assertion = Assert.Single(Translate("pm.expect(pm.response.text()).to.include(\"created\");").Assertions);

        Assert.Equal(AssertionOperator.Contains, assertion.Operator);
        Assert.Equal("created", assertion.Expected);
    }

    [Fact]
    public void A_json_field_check_becomes_a_jsonpath_assertion()
    {
        var assertion = Assert.Single(Translate("pm.expect(pm.response.json().data.id).to.eql(\"42\");").Assertions);

        Assert.Equal(ResponseField.JsonBody, assertion.Source);
        Assert.Equal("$.data.id", assertion.Target);
        Assert.Equal("42", assertion.Expected);
    }

    /// <summary>
    /// Session, not Environment, whatever Postman called it. pm.environment.set is overwhelmingly used
    /// for a token, and Environment scope writes to a file that gets committed - the exact leak the
    /// rest of this codebase exists to avoid. A user who wants it persisted can say so; one who does
    /// not cannot un-commit it.
    /// </summary>
    [Theory]
    [InlineData("pm.environment.set(\"token\", pm.response.json().access_token);")]
    [InlineData("pm.collectionVariables.set(\"token\", pm.response.json().access_token);")]
    [InlineData("pm.globals.set(\"token\", pm.response.json().access_token);")]
    public void A_variable_set_becomes_a_session_capture(string line)
    {
        var capture = Assert.Single(Translate(line).Captures);

        Assert.Equal("token", capture.VariableName);
        Assert.Equal("$.access_token", capture.Expression);
        Assert.Equal(CaptureScope.Session, capture.Scope);
    }

    /// <summary>The wrapper and its braces are structure, not logic - reporting them as untranslated
    /// would bury the lines that carry meaning under boilerplate.</summary>
    [Fact]
    public void The_test_wrapper_and_comments_are_not_reported_as_untranslated()
    {
        var translation = Translate(
            "// check the status",
            "pm.test(\"status is ok\", function () {",
            "    pm.response.to.have.status(200);",
            "});");

        Assert.Single(translation.Assertions);
        Assert.Empty(translation.Untranslated);
    }

    /// <summary>The honest half. Real logic this cannot express is handed back verbatim.</summary>
    [Fact]
    public void Real_logic_is_reported_rather_than_guessed_at()
    {
        var translation = Translate(
            "const items = pm.response.json().items;",
            "if (items.length > 3) { pm.expect(items[0].id).to.be.above(10); }");

        Assert.Empty(translation.Assertions);
        Assert.Equal(2, translation.Untranslated.Count);
        Assert.Contains("items.length", translation.Untranslated[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_mixing_both_translates_what_it_can_and_reports_the_rest()
    {
        var translation = Translate(
            "pm.test(\"ok\", function () {",
            "    pm.response.to.have.status(200);",
            "    pm.environment.set(\"token\", pm.response.json().token);",
            "    doSomethingClever(pm.response);",
            "});");

        Assert.Single(translation.Assertions);
        Assert.Single(translation.Captures);
        Assert.Equal("doSomethingClever(pm.response);", Assert.Single(translation.Untranslated));
    }

    [Fact]
    public void An_empty_script_translates_to_nothing()
    {
        var translation = PostmanScriptTranslation.Translate(null);

        Assert.False(translation.AnythingTranslated);
        Assert.Empty(translation.Untranslated);
    }
}
