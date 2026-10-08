using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;

namespace Prompuff.Infrastructure.Storage;

/// <summary>Source-generated, so settings still read and write in the trimmed command-line tool.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    Converters = [typeof(CamelCaseEnumConverter<ThemePreference>), typeof(CamelCaseEnumConverter<UpdateChannel>),
        typeof(CamelCaseEnumConverter<LibraryLayout>), typeof(CamelCaseEnumConverter<Density>), typeof(CamelCaseEnumConverter<Application.DTOs.PromptSort>)])]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Enums as camelCase strings ("dark", "lastEdited"), as settings files have always stored them.</summary>
internal sealed class CamelCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;

public sealed class JsonSettingsStore(IAppDataPathProvider paths, ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    private static readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo<AppSettings> Options = SettingsJsonContext.Default.AppSettings;

    public AppSettings Load()
    {
        var path = paths.GetSettingsPath();
        if (!File.Exists(path))
        {
            return AppSettings.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? AppSettings.Default;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't read settings; using defaults");
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var path = paths.GetSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Couldn't save settings");
        }
    }
}
