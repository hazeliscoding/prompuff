using System.Text;
using System.Text.RegularExpressions;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;

namespace Prompuff.Application.Services;

/// <summary>
/// Handles <c>{{variable_name}}</c> placeholders. Names start with a letter or underscore and contain letters,
/// digits and underscores. Spaces or tabs inside the braces are ignored. Names are case-sensitive.
/// </summary>
public sealed partial class PromptTemplateService : IPromptTemplateService
{
    [GeneratedRegex(@"\{\{[ \t]*([A-Za-z_][A-Za-z0-9_]*)[ \t]*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableToken();

    public IReadOnlyList<string> ExtractVariables(string template) =>
        AnalyzeVariables(template).Select(variable => variable.Name).ToList();

    public IReadOnlyList<TemplateVariable> AnalyzeVariables(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return [];
        }

        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in VariableToken().Matches(template))
        {
            var name = match.Groups[1].Value;
            if (counts.TryGetValue(name, out var count))
            {
                counts[name] = count + 1;
            }
            else
            {
                counts[name] = 1;
                order.Add(name);
            }
        }

        return order.Select(name => new TemplateVariable(name, counts[name])).ToList();
    }

    public string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return VariableToken().Replace(template, match =>
            TryGetValue(variables, match.Groups[1].Value, out var value) ? value : match.Value);
    }

    public IReadOnlyList<TemplateSegment> RenderSegments(string template, IReadOnlyDictionary<string, string> variables)
    {
        var segments = new List<TemplateSegment>();
        if (string.IsNullOrEmpty(template))
        {
            return segments;
        }

        var literal = new StringBuilder();
        var position = 0;
        foreach (Match match in VariableToken().Matches(template))
        {
            literal.Append(template, position, match.Index - position);
            position = match.Index + match.Length;

            if (literal.Length > 0)
            {
                segments.Add(new TemplateSegment(TemplateSegmentKind.Text, literal.ToString()));
                literal.Clear();
            }

            var name = match.Groups[1].Value;
            segments.Add(TryGetValue(variables, name, out var value)
                ? new TemplateSegment(TemplateSegmentKind.FilledVariable, value, name)
                : new TemplateSegment(TemplateSegmentKind.MissingVariable, match.Value, name));
        }

        literal.Append(template, position, template.Length - position);
        if (literal.Length > 0)
        {
            segments.Add(new TemplateSegment(TemplateSegmentKind.Text, literal.ToString()));
        }

        return segments;
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, string> variables, string name, out string value)
    {
        if (variables.TryGetValue(name, out var found) && !string.IsNullOrEmpty(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
