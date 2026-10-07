using Prompuff.Infrastructure.ImportExport;

namespace Prompuff.Infrastructure.Tests;

public class MarkdownPromptFormatTests
{
    private static readonly MarkdownPrompt Full = new()
    {
        Title = "Angular Upgrade Planner",
        Description = "Upgrade planning prompt",
        Body = "You are helping upgrade {{repo_name}} to Angular {{target_version}}.\n\n## Steps\n\n1. Inspect the repo.",
        Notes = "This prompt works well because it forces repository inspection before implementation.",
        Tags = ["angular", "migration"],
        Collection = "Coding",
        IsFavorite = true,
        Rating = 5,
        CreatedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 10, 5, 12, 30, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Writes_the_documented_format()
    {
        var expected = """
            ---
            title: Angular Upgrade Planner
            description: Upgrade planning prompt
            tags:
              - angular
              - migration
            collection: Coding
            favorite: true
            rating: 5
            createdAt: 2026-10-05T12:00:00Z
            updatedAt: 2026-10-05T12:30:00Z
            ---

            # Prompt

            You are helping upgrade {{repo_name}} to Angular {{target_version}}.

            ## Steps

            1. Inspect the repo.

            # Notes

            This prompt works well because it forces repository inspection before implementation.

            """.Replace("\r\n", "\n");

        Assert.Equal(expected, MarkdownPromptFormat.Write(Full));
    }

    [Fact]
    public void Round_trips_every_field()
    {
        var read = MarkdownPromptFormat.Read(MarkdownPromptFormat.Write(Full), "fallback");

        Assert.Equal(Full.Title, read.Title);
        Assert.Equal(Full.Description, read.Description);
        Assert.Equal(Full.Body, read.Body);
        Assert.Equal(Full.Notes, read.Notes);
        Assert.Equal(Full.Tags, read.Tags);
        Assert.Equal(Full.Collection, read.Collection);
        Assert.Equal(Full.IsFavorite, read.IsFavorite);
        Assert.Equal(Full.Rating, read.Rating);
        Assert.Equal(Full.CreatedAt, read.CreatedAt);
        Assert.Equal(Full.UpdatedAt, read.UpdatedAt);
    }

    [Theory]
    [InlineData("Plan: phase one")]
    [InlineData("\"Quoted\" title")]
    [InlineData("#hashtag first")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("- dash first")]
    [InlineData("Back\\slash")]
    [InlineData("Ünïcödé · prompts")]
    public void Titles_that_need_quoting_survive(string title)
    {
        var read = MarkdownPromptFormat.Read(MarkdownPromptFormat.Write(Full with { Title = title }), "fallback");

        Assert.Equal(title, read.Title);
    }

    [Fact]
    public void A_body_with_its_own_notes_heading_survives_without_notes()
    {
        var prompt = Full with { Body = "Write release notes.\n\n# Notes\n\nKeep them short.", Notes = null };

        var read = MarkdownPromptFormat.Read(MarkdownPromptFormat.Write(prompt), "fallback");

        Assert.Equal(prompt.Body, read.Body);
        Assert.Null(read.Notes);
    }

    [Fact]
    public void Missing_optional_metadata_is_fine()
    {
        var read = MarkdownPromptFormat.Read("---\ntitle: Just a title\n---\n\n# Prompt\n\nDo the thing.\n", "fallback");

        Assert.Equal("Just a title", read.Title);
        Assert.Equal("Do the thing.", read.Body);
        Assert.Null(read.Description);
        Assert.Null(read.Notes);
        Assert.Empty(read.Tags);
        Assert.Null(read.Collection);
        Assert.False(read.IsFavorite);
        Assert.Null(read.Rating);
        Assert.Null(read.CreatedAt);
    }

    [Fact]
    public void Plain_markdown_without_frontmatter_uses_the_file_name()
    {
        var read = MarkdownPromptFormat.Read("Summarize {{document}} in three bullets.\r\n", "summarize-document");

        Assert.Equal("summarize-document", read.Title);
        Assert.Equal("Summarize {{document}} in three bullets.", read.Body);
    }

    [Fact]
    public void Accepts_common_yaml_variations()
    {
        const string text = """
            ---
            # exported elsewhere
            title: 'It''s a title'
            tags: [Angular, "code review"]
            favorite: yes
            rating: 9
            createdAt: not a date
            description: |
              First line
              second line
            unknown: ignored
            ---
            Body without a heading.
            """;

        var read = MarkdownPromptFormat.Read(text, "fallback");

        Assert.Equal("It's a title", read.Title);
        Assert.Equal(["Angular", "code review"], read.Tags);
        Assert.True(read.IsFavorite);
        Assert.Null(read.Rating);
        Assert.Null(read.CreatedAt);
        Assert.Equal("First line\nsecond line", read.Description);
        Assert.Equal("Body without a heading.", read.Body);
    }

    [Fact]
    public void Unclosed_frontmatter_is_malformed()
    {
        var error = Assert.Throws<MarkdownFormatException>(() => MarkdownPromptFormat.Read("---\ntitle: Broken\n\n# Prompt\nBody", "fallback"));

        Assert.Contains("never closed", error.Message);
    }

    [Fact]
    public void Garbage_frontmatter_is_malformed()
    {
        Assert.Throws<MarkdownFormatException>(() => MarkdownPromptFormat.Read("---\nthis is not yaml at all\n---\nBody", "fallback"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n")]
    [InlineData("---\n---\n")]
    public void Empty_files_are_rejected(string text)
    {
        var error = Assert.Throws<MarkdownFormatException>(() => MarkdownPromptFormat.Read(text, "fallback"));

        Assert.Equal("The file is empty.", error.Message);
    }

    [Fact]
    public void Binary_files_are_rejected()
    {
        Assert.Throws<MarkdownFormatException>(() => MarkdownPromptFormat.Read("PK\u0003\u0004\0\0binary", "fallback"));
    }
}
