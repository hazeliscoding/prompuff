using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Prompuff.Cli.Tests;

/// <summary>
/// Recordings of how a release answered, in <c>Fixtures/</c>, and the rule later versions are held to: everything the
/// recording holds is still there, with the same value, and anything may be added beside it.
/// </summary>
internal static class Contract
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The library both recordings ran against: the 1.0 library fixture from Prompuff.Infrastructure.Tests.</summary>
    public const string Library = "schema-7.db";

    public static JsonObject Read(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)))!.AsObject();

    public static void Write(string path, JsonObject contract) =>
        File.WriteAllText(path, contract.ToJsonString(Options) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>An ID in the library fixture, which <c>SchemaFixtures</c> makes from a key the same way.</summary>
    public static string FixtureId(string key) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(key))).ToString("D");

    public static string Text(JsonNode? node) => node!.GetValue<string>();

    /// <summary>
    /// Lists where <paramref name="current"/> falls short of <paramref name="recorded"/>: a property that's gone, an array of
    /// another length, or a different value. Extra properties are fine. With <paramref name="shapeOnly"/>, values only
    /// have to be of the same kind, for answers that hold a new ID each time.
    /// </summary>
    public static List<string> Differences(JsonNode? recorded, JsonNode? current, bool shapeOnly = false)
    {
        var differences = new List<string>();
        Compare(recorded, current, "$", shapeOnly, differences);
        return differences;
    }

    /// <summary>Arguments recorded as JSON, in the form the MCP client sends.</summary>
    public static Dictionary<string, object?> Arguments(JsonNode? arguments) =>
        (arguments?.AsObject() ?? []).ToDictionary(
            pair => pair.Key,
            pair => (object?)JsonDocument.Parse(pair.Value?.ToJsonString() ?? "null").RootElement.Clone());

    private static void Compare(JsonNode? recorded, JsonNode? current, string path, bool shapeOnly, List<string> differences)
    {
        switch (recorded)
        {
            case null when current is not null:
                differences.Add($"{path} was null in 1.0 and is now {current.ToJsonString()}");
                break;
            case JsonObject before when current is JsonObject now:
                foreach (var (name, value) in before)
                {
                    if (now.ContainsKey(name))
                    {
                        Compare(value, now[name], $"{path}.{name}", shapeOnly, differences);
                    }
                    else
                    {
                        differences.Add($"{path}.{name} is missing");
                    }
                }

                break;
            case JsonArray before when current is JsonArray now:
                if (before.Count != now.Count)
                {
                    differences.Add($"{path} held {before.Count} items in 1.0 and now holds {now.Count}");
                    break;
                }

                for (var i = 0; i < before.Count; i++)
                {
                    Compare(before[i], now[i], $"{path}[{i}]", shapeOnly, differences);
                }

                break;
            case JsonValue before when current is JsonValue now:
                if (shapeOnly ? before.GetValueKind() != now.GetValueKind() : !JsonNode.DeepEquals(before, now))
                {
                    differences.Add($"{path} was {before.ToJsonString()} in 1.0 and is now {now.ToJsonString()}");
                }

                break;
            case not null:
                differences.Add($"{path} was {recorded.ToJsonString()} in 1.0 and is now {current?.ToJsonString() ?? "null"}");
                break;
        }
    }
}
