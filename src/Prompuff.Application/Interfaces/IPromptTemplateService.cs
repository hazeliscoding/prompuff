using Prompuff.Application.DTOs;

namespace Prompuff.Application.Interfaces;

public interface IPromptTemplateService
{
    /// <summary>Distinct variable names in order of first appearance.</summary>
    IReadOnlyList<string> ExtractVariables(string template);

    /// <summary>Distinct variables with how often each appears, in order of first appearance.</summary>
    IReadOnlyList<TemplateVariable> AnalyzeVariables(string template);

    /// <summary>Replaces each <c>{{name}}</c> that has a non-empty value. Others keep their original token.</summary>
    string Render(string template, IReadOnlyDictionary<string, string> variables);

    /// <summary>The rendered output split into literal text, filled values and unresolved tokens.</summary>
    IReadOnlyList<TemplateSegment> RenderSegments(string template, IReadOnlyDictionary<string, string> variables);
}
