using Prompuff.Domain.Entities;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Domain.Tests;

public class PromptContentTests
{
    [Fact]
    public void Blank_title_becomes_untitled()
    {
        Assert.Equal(PromptContent.UntitledTitle, new PromptContent("   ", null, "body", null).Title);
    }

    [Fact]
    public void Title_and_description_are_trimmed_to_one_line()
    {
        var content = new PromptContent("  Angular\r\nUpgrade  ", " Plans\nupgrades ", "body", null);

        Assert.Equal("Angular Upgrade", content.Title);
        Assert.Equal("Plans upgrades", content.Description);
    }

    [Fact]
    public void Blank_description_and_notes_become_null()
    {
        var content = new PromptContent("Title", "  ", "body", "\n\n");

        Assert.Null(content.Description);
        Assert.Null(content.Notes);
    }

    [Fact]
    public void Line_endings_do_not_make_contents_different()
    {
        var windows = new PromptContent("Title", null, "line one\r\nline two", "why\r\nit worked");
        var unix = new PromptContent("Title", null, "line one\nline two", "why\nit worked");

        Assert.Equal(unix, windows);
    }

    [Fact]
    public void Body_whitespace_is_significant()
    {
        Assert.NotEqual(new PromptContent("T", null, "a", null), new PromptContent("T", null, "a ", null));
    }

    [Fact]
    public void Prompt_rejects_ratings_outside_one_to_five()
    {
        var prompt = new Prompt { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UnixEpoch };

        prompt.Rating = 5;
        prompt.Rating = null;
        Assert.Throws<ArgumentOutOfRangeException>(() => prompt.Rating = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => prompt.Rating = 6);
    }

    [Theory]
    [InlineData("  Coding  ", "Coding")]
    [InlineData("Writing   and docs", "Writing and docs")]
    [InlineData("   ", null)]
    public void Collection_names_are_normalized(string raw, string? expected)
    {
        Assert.Equal(expected, Collection.NormalizeName(raw));
    }
}
