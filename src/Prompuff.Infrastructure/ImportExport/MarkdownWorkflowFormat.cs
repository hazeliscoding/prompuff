using System.Text;
using System.Text.RegularExpressions;

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
public static partial class MarkdownWorkflowFormat
{
    private const string HandsOff = "**Hands off:**";

    [GeneratedRegex(@"^##\s+(?:Step\s+\d+\s*[:.\-–—]\s*)?(?<title>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex StepHeading();

    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})\s*(?<info>\S*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FenceLine();

    [GeneratedRegex("`+", RegexOptions.CultureInvariant)]
    private static partial Regex BacktickRun();

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
            var longest = BacktickRun().Matches(body).Select(match => match.Length).DefaultIfEmpty(0).Max();
            var fence = new string('`', Math.Max(3, longest + 1));

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
                var closing = FenceLine().Match(line);
                if (closing.Success && closing.Groups["info"].Length == 0 && closing.Groups["fence"].Value[0] == open.Mark && closing.Groups["fence"].Length >= open.Length)
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

            if (StepHeading().Match(line) is { Success: true } heading)
            {
                Finish();
                stepTitle = heading.Groups["title"].Value;
                body = null;
                note = [];
                continue;
            }

            if (stepTitle is null)
            {
                continue;
            }

            if (FenceLine().Match(line) is { Success: true } opening && body is null)
            {
                var marks = opening.Groups["fence"].Value;
                fence = (marks[0], marks.Length);
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
