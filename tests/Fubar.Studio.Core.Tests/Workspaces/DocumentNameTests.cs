using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Core.Tests.Workspaces;

/// <summary>
/// What may name a file in a workspace.
///
/// <para>The rule is about the FORMAT, not the host. A workspace is a folder of files meant to be
/// committed and shared, so a name has to be one every platform it can be checked out on will accept -
/// which is why none of this asks <c>Path.GetInvalidFileNameChars</c>, whose answer differs between
/// the machine a case is created on and the machine it is opened on.</para>
///
/// <para>That is not hypothetical: it shipped the other way round, and the Linux CI runner caught two
/// tests that passed on Windows because Windows happens to reject what the code forgot to.</para>
/// </summary>
public class DocumentNameTests
{
    [Theory]
    [InlineData("smoke")]
    [InlineData("not-found")]
    [InlineData("page 2")]
    [InlineData("Ordrer_Søk")]
    [InlineData("v1.2")]
    public void An_ordinary_name_is_fine(string name) => Assert.True(DocumentName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_a_name(string? name) => Assert.False(DocumentName.IsValid(name));

    [Theory]
    [InlineData("nested/name")]
    [InlineData(@"nested\name")]
    [InlineData("a:b")]
    [InlineData("star*")]
    [InlineData("what?")]
    [InlineData("quote\"d")]
    [InlineData("less<than")]
    [InlineData("more>than")]
    [InlineData("pipe|d")]
    public void A_character_Windows_refuses_is_refused_everywhere(string name)
    {
        // On Linux every one of these but the slash is a legal file name, so a case named "what?"
        // created there is a repository nobody on Windows can check out - and the machine that made it
        // is the one machine where nothing looks wrong.
        Assert.False(DocumentName.IsValid(name));
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    public void A_control_character_is_refused(string name) => Assert.False(DocumentName.IsValid(name));

    [Theory]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void A_trailing_dot_or_space_is_refused(string name)
    {
        // Windows strips both, so "orders " and "orders" are one file there and two here - and the
        // second to be written would overwrite a file its author never named.
        Assert.False(DocumentName.IsValid(name));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void A_directory_entry_is_not_a_name(string name) => Assert.False(DocumentName.IsValid(name));

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("aux")]
    public void A_reserved_device_name_is_refused(string name)
    {
        // CON.json is not a file on Windows and never will be. The checkout fails with an error naming
        // no file anyone recognises, which is a bad afternoon for whoever pulls it.
        Assert.False(DocumentName.IsValid(name));
    }

    [Fact]
    public void A_reserved_name_with_more_after_it_is_ordinary()
    {
        // Only the stem is reserved: "console" and "context" are not devices.
        Assert.True(DocumentName.IsValid("console"));
        Assert.True(DocumentName.IsValid("nullable"));
    }

    // ---- Sanitize --------------------------------------------------------------------------------

    [Fact]
    public void Sanitizing_replaces_what_it_cannot_keep()
    {
        // For names this app DERIVES - a folder from an OpenAPI tag, an environment name that becomes a
        // snapshot's file name. There is nobody to tell, so it is made usable rather than failing an
        // import over a colon in a tag.
        Assert.Equal("Pets_store", DocumentName.Sanitize("Pets/store"));
        Assert.Equal("v1_2", DocumentName.Sanitize("v1:2"));
    }

    [Fact]
    public void Sanitizing_trims_the_ends_a_file_system_would()
    {
        Assert.Equal("orders", DocumentName.Sanitize("  orders  "));
        Assert.Equal("orders", DocumentName.Sanitize("orders."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    public void Sanitizing_something_with_nothing_usable_in_it_falls_back(string name) =>
        Assert.Equal("Imported", DocumentName.Sanitize(name, "Imported"));

    [Fact]
    public void A_name_that_was_ALL_replacements_still_counts_as_a_name()
    {
        // "___" carries nothing, but it is what the old sanitiser returned and it is stable: the same
        // input names the same folder on the next import, which the fallback cannot promise once two
        // different inputs both collapse to it.
        Assert.Equal("___", DocumentName.Sanitize("///", "Imported"));
    }

    [Fact]
    public void Sanitizing_a_reserved_name_keeps_it_readable()
    {
        // "_" rather than the fallback: the name was meaningful and only the device collision is in
        // the way, so "CON_" still says which tag it came from.
        Assert.Equal("CON_", DocumentName.Sanitize("CON"));
    }

    [Fact]
    public void Everything_Sanitize_returns_is_a_valid_name()
    {
        string[] awkward = ["a:b", "  ", "..", "CON", "trailing.", "two\nlines", @"c:\x\y", "///"];

        Assert.All(awkward, name => Assert.True(
            DocumentName.IsValid(DocumentName.Sanitize(name)),
            $"Sanitize({name}) produced a name IsValid rejects"));
    }
}
