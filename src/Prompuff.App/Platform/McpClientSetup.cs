using System.Text.Encodings.Web;
using System.Text.Json;

namespace Prompuff.App.Platform;

public enum McpClient
{
    ClaudeCode,
    ClaudeDesktop,
    CopilotCli,
    Codex,
}

/// <summary>A ready-to-paste setup that points one AI tool at <c>prompuff mcp</c>.</summary>
public sealed record McpClientSetup(McpClient Client, string Name, string Steps, string Snippet)
{
    public static IReadOnlyList<McpClient> Clients { get; } = [McpClient.ClaudeCode, McpClient.ClaudeDesktop, McpClient.CopilotCli, McpClient.Codex];

    /// <param name="command">The full path of the CLI, so tools started outside a terminal find it too.</param>
    public static McpClientSetup For(McpClient client, string command) => client switch
    {
        McpClient.ClaudeCode => new(client, "Claude Code",
            "Run this in a terminal. Favorites show up as /mcp__prompuff__… commands.",
            $"claude mcp add --scope user prompuff -- {Quote(command)} mcp"),
        McpClient.ClaudeDesktop => new(client, "Claude Desktop",
            "In Claude Desktop, open Settings › Developer › Edit Config, add the prompuff entry to \"mcpServers\", then restart it. Favorites show up in the + menu.",
            ClaudeDesktopJson(command)),
        McpClient.CopilotCli => new(client, "Copilot CLI",
            "Run this in a terminal. It adds Prompuff to ~/.copilot/mcp-config.json.",
            $"copilot mcp add prompuff -- {Quote(command)} mcp"),
        McpClient.Codex => new(client, "Codex",
            "Run this in a terminal. It adds Prompuff to ~/.codex/config.toml.",
            $"codex mcp add prompuff -- {Quote(command)} mcp"),
        _ => throw new ArgumentOutOfRangeException(nameof(client)),
    };

    /// <summary>Double quotes on Windows, which PowerShell and cmd both read; single quotes for POSIX shells, which keep <c>$</c> literal.</summary>
    internal static string Quote(string path) => OperatingSystem.IsWindows()
        ? $"\"{path}\""
        : "'" + path.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ClaudeDesktopJson(string command) =>
        $$"""
        {
          "mcpServers": {
            "prompuff": {
              "command": {{JsonSerializer.Serialize(command, Json)}},
              "args": ["mcp"]
            }
          }
        }
        """;
}
