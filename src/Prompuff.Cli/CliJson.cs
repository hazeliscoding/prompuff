using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prompuff.Application.DTOs;
using Prompuff.Domain.Entities;

namespace Prompuff.Cli;

/// <summary>A prompt in a list: <c>search</c>, <c>list</c>, and the MCP search tool.</summary>
internal sealed record PromptListItem(
    string Id,
    string Title,
    string? Description,
    string? Collection,
    IReadOnlyList<string> Tags,
    bool Favorite,
    int? Rating,
    string UpdatedAt)
{
    public static PromptListItem From(PromptSummary summary, IReadOnlyDictionary<Guid, string> collections) => new(
        summary.Id.ToString("D"),
        summary.Title,
        summary.Description,
        summary.CollectionId is { } id ? collections.GetValueOrDefault(id) : null,
        summary.Tags,
        summary.IsFavorite,
        summary.Rating,
        CliJson.Time(summary.UpdatedAt));
}

/// <summary>Everything about one prompt: <c>get --json</c> and the MCP get tool.</summary>
internal sealed record PromptDetails(
    string Id,
    string Title,
    string? Description,
    string Body,
    string? Notes,
    IReadOnlyList<string> Variables,
    string? Collection,
    IReadOnlyList<string> Tags,
    bool Favorite,
    int? Rating,
    string CreatedAt,
    string UpdatedAt)
{
    public static PromptDetails From(Prompt prompt, IReadOnlyList<string> variables, string? collection) => new(
        prompt.Id.ToString("D"),
        prompt.Title,
        prompt.Description,
        prompt.Body,
        prompt.Notes,
        variables,
        collection,
        prompt.Tags,
        prompt.IsFavorite,
        prompt.Rating,
        CliJson.Time(prompt.CreatedAt),
        CliJson.Time(prompt.UpdatedAt));
}

internal sealed record RenderedPrompt(string Id, string Title, string Rendered, IReadOnlyList<string> Missing);

internal sealed record SavedPrompt(string Id, string Title);

// One line ending everywhere, so scripts and AI tools see the same output on every platform.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, NewLine = "\n")]
[JsonSerializable(typeof(List<PromptListItem>))]
[JsonSerializable(typeof(PromptDetails))]
[JsonSerializable(typeof(RenderedPrompt))]
[JsonSerializable(typeof(SavedPrompt))]
internal sealed partial class CliJsonContext : JsonSerializerContext;

internal static class CliJson
{
    // Keeps curly quotes and accents readable in the output instead of escaping them.
    private static readonly CliJsonContext Context = new(new JsonSerializerOptions(CliJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string Serialize(List<PromptListItem> value) => JsonSerializer.Serialize(value, Context.ListPromptListItem);

    public static string Serialize(PromptDetails value) => JsonSerializer.Serialize(value, Context.PromptDetails);

    public static string Serialize(RenderedPrompt value) => JsonSerializer.Serialize(value, Context.RenderedPrompt);

    public static string Serialize(SavedPrompt value) => JsonSerializer.Serialize(value, Context.SavedPrompt);

    public static string Time(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
