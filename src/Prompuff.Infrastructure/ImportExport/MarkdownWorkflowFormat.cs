using System.Text;

namespace Prompuff.Infrastructure.ImportExport;

/// <summary>A workflow as one portable Markdown document.</summary>
public sealed record MarkdownWorkflow(string Title, string? Description, IReadOnlyList<MarkdownWorkflowStep> Steps);

/// <param name="Title">The prompt's title.</param>
/// <param name="Body">The prompt itself, exactly as in the library.</param>
/// <param name="Note">What the step hands to the next.</param>
public sealed record MarkdownWorkflowStep(string Title, string Body, string? Note);

/// <summary>
/// Writes a workflow as frontmatter with <c>type: workflow</c>, then a <c>## Step N: Title</c> section per step holding
/// the prompt in a fenced <c>prompt</c> block and the hand-off note as a quote. The fence is longer than any run of
/// backticks in the prompt, so prompts that contain code blocks survive. It reads like any Markdown on GitHub.
/// </summary>
public static class MarkdownWorkflowFormat
{
    private const string HandsOff = "**Hands off:**";

    /// <summary>True when the text's frontmatter says <c>type: workflow</c>.</summary>
    public static bool IsWorkflow(string text)
    {
        try
        {
            return IsWorkflowFrontmatter(text);
        }
        catch (MarkdownFormatException)
        {
            // Not readable as a workflow; the prompt reader explains what's wrong with the file.
            return false;
        }
    }

    private static bool IsWorkflowFrontmatter(string text)
    {
        var fields = ReadFrontmatter(MarkdownPromptFormat.Normalize(text).TrimStart('﻿').Split('\n'), out _);
        return string.Equals(MarkdownPromptFormat.GetString(fields, "type")?.Trim(), "workflow", StringComparison.OrdinalIgnoreCase);
    }

    public static string Write(MarkdownWorkflow workflow)
    {
        var text = new StringBuilder();
        text.Append("---\n");
        text.Append("type: workflow\n");
        text.Append("title: ").Append(MarkdownPromptFormat.Quote(workflow.Title)).Append('\n');
        if (!string.IsNullOrWhiteSpace(workflow.Description))
        {
            text.Append("description: ").Append(MarkdownPromptFormat.Quote(workflow.Description)).Append('\n');
        }

        text.Append("---\n\n");
        text.Append("# ").Append(workflow.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(workflow.Description))
        {
            text.Append(workflow.Description.Trim()).Append("\n\n");
        }

        for (var i = 0; i < workflow.Steps.Count; i++)
        {
            var step = workflow.Steps[i];
            var body = MarkdownPromptFormat.Normalize(step.Body).Trim('\n');
            var fence = new string('`', Math.Max(3, LongestBacktickRun(body) + 1));

            text.Append("## Step ").Append(i + 1).Append(": ").Append(step.Title.Replace('\n', ' ').Trim()).Append("\n\n");
            text.Append(fence).Append("prompt\n").Append(body).Append('\n').Append(fence).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(step.Note))
            {
                var lines = MarkdownPromptFormat.Normalize(step.Note).Trim().Split('\n');
                text.Append("> ").Append(HandsOff).Append(' ').Append(lines[0]).Append('\n');
                foreach (var line in lines.Skip(1))
                {
                    text.Append('>').Append(line.Length == 0 ? string.Empty : " " + line).Append('\n');
                }

                text.Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    /// <param name="fallbackTitle">Used when the document has no title, usually the file name.</param>
    public static MarkdownWorkflow Read(string text, string fallbackTitle)
    {
        var lines = MarkdownPromptFormat.Normalize(text).TrimStart('﻿').Split('\n');
        var fields = ReadFrontmatter(lines, out var start);
        var title = MarkdownPromptFormat.GetString(fields, "title");
        var description = MarkdownPromptFormat.GetString(fields, "description");

        var steps = new List<MarkdownWorkflowStep>();
        string? stepTitle = null;
        List<string>? body = null;
        var note = new List<string>();
        (char Mark, int Length)? fence = null;

        void Finish()
        {
            if (stepTitle is null)
            {
                return;
            }

            if (body is null)
            {
                throw new MarkdownFormatException($"Step {steps.Count + 1} (“{stepTitle}”) has no prompt in a ```prompt block.");
            }

            var noteText = string.Join('\n', note).Trim();
            steps.Add(new MarkdownWorkflowStep(stepTitle, string.Join('\n', body).Trim('\n').TrimEnd(), noteText.Length == 0 ? null : noteText));
        }

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (fence is { } open)
            {
                if (ReadFence(line) is { Info.Length: 0 } closing && closing.Mark == open.Mark && closing.Length >= open.Length)
                {
                    fence = null;
                }
                else
                {
                    body!.Add(line);
                }

                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal) && stepTitle is null && title is null)
            {
                title = line[2..].Trim();
                continue;
            }

            if (ReadStepHeading(line) is { } heading)
            {
                Finish();
                stepTitle = heading;
                body = null;
                note = [];
                continue;
            }

            if (stepTitle is null)
            {
                continue;
            }

            if (body is null && ReadFence(line) is { } opening)
            {
                fence = (opening.Mark, opening.Length);
                body = [];
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var quoted = line.TrimStart()[1..];
                quoted = quoted.StartsWith(' ') ? quoted[1..] : quoted;
                if (note.Count == 0 && quoted.StartsWith(HandsOff, StringComparison.OrdinalIgnoreCase))
                {
                    quoted = quoted[HandsOff.Length..].TrimStart();
                }

                note.Add(quoted);
            }
        }

        if (fence is not null)
        {
            throw new MarkdownFormatException($"The prompt in step {steps.Count + 1} is never closed.");
        }

        Finish();
        if (steps.Count == 0)
        {
            throw new MarkdownFormatException("This workflow has no steps. Each one starts with a “## Step 1: Title” heading.");
        }

        return new MarkdownWorkflow(string.IsNullOrWhiteSpace(title) ? fallbackTitle : title.Trim(), description, steps);
    }

    // The line readers below are single forward scans, so a crafted file can't make them slow the way an
    // overlapping regex such as \s+(.+?)\s*$ can.

    /// <summary>Reads "## Step 2: Title" or "## Title", returning the title, or null for any other line.</summary>
    private static string? ReadStepHeading(string line)
    {
        if (line.Length < 3 || !line.StartsWith("##", StringComparison.Ordinal) || !char.IsWhiteSpace(line[2]))
        {
            return null;
        }

        var rest = line.AsSpan(2).Trim();
        if (rest.StartsWith("Step", StringComparison.Ordinal) && rest.Length > 4 && char.IsWhiteSpace(rest[4]))
        {
            var i = 4;
            while (i < rest.Length && char.IsWhiteSpace(rest[i]))
            {
                i++;
            }

            var digits = i;
            while (i < rest.Length && char.IsAsciiDigit(rest[i]))
            {
                i++;
            }

            while (i > digits && i < rest.Length && char.IsWhiteSpace(rest[i]))
            {
                i++;
            }

            if (i > digits && i < rest.Length && rest[i] is ':' or '.' or '-' or '–' or '—' && rest[(i + 1)..].Trim() is { Length: > 0 } title)
            {
                return title.ToString();
            }
        }

        return rest.Length == 0 ? null : rest.ToString();
    }

    /// <summary>Reads a fence: up to three spaces, three or more backticks or tildes, then at most one info word.</summary>
    private static (char Mark, int Length, string Info)? ReadFence(string line)
    {
        var i = 0;
        while (i < 3 && i < line.Length && line[i] == ' ')
        {
            i++;
        }

        if (i >= line.Length || line[i] is not ('`' or '~'))
        {
            return null;
        }

        var mark = line[i];
        var start = i;
        while (i < line.Length && line[i] == mark)
        {
            i++;
        }

        var info = line.AsSpan(i).Trim();
        if (i - start < 3 || info.IndexOfAny(' ', '\t') >= 0)
        {
            return null;
        }

        return (mark, i - start, info.ToString());
    }

    private static int LongestBacktickRun(string text)
    {
        int longest = 0, run = 0;
        foreach (var ch in text)
        {
            run = ch == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static Dictionary<string, object> ReadFrontmatter(string[] lines, out int contentStart)
    {
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        contentStart = 0;
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
        {
            return fields;
        }

        var end = Array.FindIndex(lines, 1, line => line.TrimEnd() is "---" or "...");
        if (end < 0)
        {
            throw new MarkdownFormatException("The metadata block at the top of the file is never closed with ---.");
        }

        MarkdownPromptFormat.ParseFrontmatter(lines[1..end], fields);
        contentStart = end + 1;
        return fields;
    }
}
