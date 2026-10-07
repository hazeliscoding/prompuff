using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Application.Tests;

public class LineDiffTests
{
    [Fact]
    public void Identical_texts_have_no_changes()
    {
        var lines = LineDiff.Compute("a\nb", "a\nb");

        Assert.All(lines, line => Assert.Equal(DiffLineKind.Unchanged, line.Kind));
        Assert.Equal((0, 0), LineDiff.CountChanges("a\nb", "a\nb"));
    }

    [Fact]
    public void Marks_added_removed_and_unchanged_lines_with_numbers()
    {
        var lines = LineDiff.Compute("one\ntwo\nthree", "one\n2\nthree\nfour");

        Assert.Equal(
            [
                new DiffLine(DiffLineKind.Unchanged, "one", 1, 1),
                new DiffLine(DiffLineKind.Removed, "two", 2, null),
                new DiffLine(DiffLineKind.Added, "2", null, 2),
                new DiffLine(DiffLineKind.Unchanged, "three", 3, 3),
                new DiffLine(DiffLineKind.Added, "four", null, 4),
            ],
            lines);
    }

    [Fact]
    public void Empty_old_text_is_all_additions()
    {
        Assert.Equal((2, 0), LineDiff.CountChanges(null, "a\nb"));
    }

    [Fact]
    public void Version_notes_describe_what_changed()
    {
        var before = new PromptContent("Planner", null, "a\nb", null);

        Assert.Equal("Edited body (+1 −1 lines)", VersionNotes.Describe(before, new PromptContent("Planner", null, "a\nc", null)));
        Assert.Equal("Edited title and notes", VersionNotes.Describe(before, new PromptContent("Planner v2", null, "a\nb", "why")));
        Assert.Equal(
            "Edited title, description and body (+1 −0 lines)",
            VersionNotes.Describe(before, new PromptContent("New", "desc", "a\nb\nc", null)));
    }
}
