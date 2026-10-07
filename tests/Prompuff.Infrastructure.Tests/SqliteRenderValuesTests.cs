using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.Infrastructure.Tests;

public class SqliteRenderValuesTests
{
    [Fact]
    public async Task Values_are_kept_per_prompt_and_empty_ones_are_dropped()
    {
        await using var library = await TestLibrary.CreateAsync();
        var values = new SqliteRenderValues(library.Database);
        var first = await library.PromptService.CreateAsync(new PromptContent("First", null, "{{repo}} {{Repo}}", null));
        var second = await library.PromptService.CreateAsync(new PromptContent("Second", null, "{{repo}}", null));

        await values.SaveAsync(first.Id, new Dictionary<string, string> { ["repo"] = "acme", ["Repo"] = "Acme", ["empty"] = "" });
        await values.SaveAsync(second.Id, new Dictionary<string, string> { ["repo"] = "other" });
        await values.SaveAsync(first.Id, new Dictionary<string, string> { ["repo"] = "acme/web", ["Repo"] = "Acme" });

        Assert.Equal(new Dictionary<string, string> { ["repo"] = "acme/web", ["Repo"] = "Acme" }, await values.LoadAsync(first.Id));
        Assert.Equal(new Dictionary<string, string> { ["repo"] = "other" }, await values.LoadAsync(second.Id));
    }

    [Fact]
    public async Task Clearing_forgets_one_prompts_values()
    {
        await using var library = await TestLibrary.CreateAsync();
        var values = new SqliteRenderValues(library.Database);
        var prompt = await library.PromptService.CreateAsync(new PromptContent("First", null, "{{repo}}", null));
        await values.SaveAsync(prompt.Id, new Dictionary<string, string> { ["repo"] = "acme" });

        await values.ClearAsync(prompt.Id);

        Assert.Empty(await values.LoadAsync(prompt.Id));
    }

    [Fact]
    public async Task Values_go_when_the_prompt_is_removed_for_good()
    {
        await using var library = await TestLibrary.CreateAsync();
        var values = new SqliteRenderValues(library.Database);
        var prompt = await library.PromptService.CreateAsync(new PromptContent("First", null, "{{repo}}", null));
        await values.SaveAsync(prompt.Id, new Dictionary<string, string> { ["repo"] = "acme" });

        await library.PromptService.DeleteAsync(prompt.Id);
        Assert.NotEmpty(await values.LoadAsync(prompt.Id));
        await library.PromptService.EmptyRecentlyDeletedAsync();

        Assert.Empty(await values.LoadAsync(prompt.Id));
    }
}
