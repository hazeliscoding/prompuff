using Prompuff.Domain.ValueObjects;

namespace Prompuff.Domain.Tests;

public class TagNameTests
{
    [Theory]
    [InlineData("Angular", "angular")]
    [InlineData("angular", "angular")]
    [InlineData(" angular", "angular")]
    [InlineData("  ANGULAR  ", "angular")]
    [InlineData("#angular", "angular")]
    [InlineData("# angular", "angular")]
    [InlineData("code review", "code-review")]
    [InlineData("code   review\t", "code-review")]
    [InlineData("UI/UX", "ui/ux")]
    [InlineData("c#", "c#")]
    public void Normalize_produces_a_canonical_form(string raw, string expected)
    {
        Assert.Equal(expected, TagName.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData(" # ")]
    public void Normalize_returns_null_when_nothing_is_left(string? raw)
    {
        Assert.Null(TagName.Normalize(raw));
    }

    [Fact]
    public void Normalize_caps_the_length()
    {
        var normalized = TagName.Normalize(new string('a', 100));

        Assert.Equal(TagName.MaxLength, normalized!.Length);
    }

    [Fact]
    public void NormalizeAll_removes_obvious_duplicates_and_keeps_first_seen_order()
    {
        var tags = TagName.NormalizeAll(["Angular", "migration", "angular", " angular", "#Migration", "", null, "review"]);

        Assert.Equal(["angular", "migration", "review"], tags);
    }
}
