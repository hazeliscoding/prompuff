using Prompuff.Application.DTOs;
using Prompuff.Application.Services;

namespace Prompuff.Application.Tests;

public class PromptTemplateServiceTests
{
    private readonly PromptTemplateService _service = new();

    [Fact]
    public void Extracts_a_single_variable()
    {
        Assert.Equal(["name"], _service.ExtractVariables("Hello {{name}}!"));
    }

    [Fact]
    public void Extracts_multiple_variables_in_order_of_first_appearance()
    {
        var variables = _service.ExtractVariables("Upgrade {{repo_name}} to Angular {{target_version}} with {{package_manager}}.");

        Assert.Equal(["repo_name", "target_version", "package_manager"], variables);
    }

    [Fact]
    public void Duplicate_variables_are_listed_once_and_counted()
    {
        const string template = "{{tone}} and {{repo}} then {{tone}} again, {{ tone }}";

        Assert.Equal(["tone", "repo"], _service.ExtractVariables(template));
        Assert.Equal([new TemplateVariable("tone", 3), new TemplateVariable("repo", 1)], _service.AnalyzeVariables(template));
    }

    [Theory]
    [InlineData("{{project_name}}", "project_name")]
    [InlineData("{{_private}}", "_private")]
    [InlineData("{{v2_name_3}}", "v2_name_3")]
    public void Underscores_and_digits_are_allowed(string template, string expected)
    {
        Assert.Equal([expected], _service.ExtractVariables(template));
    }

    [Theory]
    [InlineData("{{ repo_name }}")]
    [InlineData("{{repo_name }}")]
    [InlineData("{{\trepo_name\t}}")]
    public void Whitespace_inside_the_braces_is_ignored(string template)
    {
        Assert.Equal(["repo_name"], _service.ExtractVariables(template));
        Assert.Equal("acme", _service.Render(template, new Dictionary<string, string> { ["repo_name"] = "acme" }));
    }

    [Theory]
    [InlineData("{{1st}}")]
    [InlineData("{{has space}}")]
    [InlineData("{{has-dash}}")]
    [InlineData("{single}")]
    [InlineData("{{}}")]
    public void Tokens_that_are_not_variables_are_ignored(string template)
    {
        Assert.Empty(_service.ExtractVariables(template));
        Assert.Equal(template, _service.Render(template, new Dictionary<string, string>()));
    }

    [Fact]
    public void Names_are_case_sensitive()
    {
        Assert.Equal(["Name", "name"], _service.ExtractVariables("{{Name}} {{name}}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_templates_are_handled(string? template)
    {
        Assert.Empty(_service.ExtractVariables(template!));
        Assert.Equal(string.Empty, _service.Render(template!, new Dictionary<string, string> { ["x"] = "y" }));
        Assert.Empty(_service.RenderSegments(template!, new Dictionary<string, string>()));
    }

    [Fact]
    public void Renders_every_occurrence()
    {
        var rendered = _service.Render(
            "You are helping upgrade {{repo_name}} to Angular {{target_version}}.\n\nRepo: {{repo_name}}",
            new Dictionary<string, string> { ["repo_name"] = "mv-dashboard", ["target_version"] = "22" });

        Assert.Equal("You are helping upgrade mv-dashboard to Angular 22.\n\nRepo: mv-dashboard", rendered);
    }

    [Fact]
    public void Missing_values_keep_the_original_token()
    {
        var rendered = _service.Render("{{a}} and {{ missing_value }}", new Dictionary<string, string> { ["a"] = "A" });

        Assert.Equal("A and {{ missing_value }}", rendered);
    }

    [Fact]
    public void Empty_values_keep_the_original_token()
    {
        Assert.Equal("{{tone}}", _service.Render("{{tone}}", new Dictionary<string, string> { ["tone"] = "" }));
    }

    [Fact]
    public void Values_are_inserted_literally_even_when_they_look_like_tokens()
    {
        var rendered = _service.Render("{{a}} {{b}}", new Dictionary<string, string> { ["a"] = "{{b}}", ["b"] = "$1" });

        Assert.Equal("{{b}} $1", rendered);
    }

    [Fact]
    public void Segments_mark_filled_and_missing_variables()
    {
        var segments = _service.RenderSegments("Hi {{name}}, meet {{friend}}.", new Dictionary<string, string> { ["name"] = "Ada" });

        Assert.Equal(
            [
                new TemplateSegment(TemplateSegmentKind.Text, "Hi "),
                new TemplateSegment(TemplateSegmentKind.FilledVariable, "Ada", "name"),
                new TemplateSegment(TemplateSegmentKind.Text, ", meet "),
                new TemplateSegment(TemplateSegmentKind.MissingVariable, "{{friend}}", "friend"),
                new TemplateSegment(TemplateSegmentKind.Text, "."),
            ],
            segments);
    }

    [Fact]
    public void Segments_join_to_the_rendered_text()
    {
        const string template = "{{a}}{{b}} x {{c}}";
        var values = new Dictionary<string, string> { ["a"] = "1", ["c"] = "3" };

        Assert.Equal(_service.Render(template, values), string.Concat(_service.RenderSegments(template, values).Select(segment => segment.Text)));
    }
}
