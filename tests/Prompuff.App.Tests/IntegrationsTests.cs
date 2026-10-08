using Avalonia.Headless.XUnit;
using Prompuff.App.Platform;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.Tests;

public class IntegrationsTests
{
    private static async Task<SettingsViewModel> OpenIntegrationsAsync(AppHarness app)
    {
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.Integrations);
        await app.SettleAsync();
        return settings;
    }

    [AvaloniaFact]
    public async Task The_command_line_tool_installs_and_removes_from_Settings()
    {
        var installer = new FakeCommandLineInstaller();
        await using var app = await AppHarness.StartAsync(importSamples: false, installer: installer);
        var settings = await OpenIntegrationsAsync(app);

        Assert.True(settings.HasBundledCli);
        Assert.False(settings.IsCliInstalled);
        Assert.False(settings.AllowMcp);
        app.Screenshot("settings-integrations");

        await settings.InstallCommandLineToolCommand.ExecuteAsync(null);
        Assert.True(installer.IsInstalled);
        Assert.True(settings.IsCliInstalled);
        Assert.Contains("prompuff help", settings.CliStatus);
        Assert.False(settings.McpNeedsInstall);

        await settings.RemoveCommandLineToolCommand.ExecuteAsync(null);
        Assert.False(installer.IsInstalled);
        Assert.False(settings.IsCliInstalled);
    }

    [AvaloniaFact]
    public async Task Copying_an_MCP_setup_offers_to_turn_MCP_on()
    {
        var installer = new FakeCommandLineInstaller { IsInstalled = true };
        await using var app = await AppHarness.StartAsync(importSamples: false, installer: installer);
        var settings = await OpenIntegrationsAsync(app);
        var quoted = McpClientSetup.Quote(installer.CommandPath);

        Assert.Equal(["Claude Code", "Claude Desktop", "Copilot CLI", "Codex"], settings.McpClients.Select(client => client.Name));
        Assert.Equal($"claude mcp add --scope user prompuff -- {quoted} mcp", settings.McpSetup.Snippet);

        settings.McpClients[3].IsSelected = true;
        await app.SettleAsync();
        Assert.Equal($"codex mcp add prompuff -- {quoted} mcp", settings.McpSetup.Snippet);
        Assert.Equal([false, false, false, true], settings.McpClients.Select(client => client.IsSelected));

        var copying = settings.CopyMcpSetupCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(settings.McpSetup.Snippet, await app.Get<IClipboardService>().GetTextAsync());
        Assert.NotNull(app.ViewModel.Dialogs.Current);
        app.Screenshot("dialog-turn-on-mcp");
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await copying;

        Assert.True(settings.AllowMcp);
        Assert.True(app.Get<ISettingsStore>().Load().AllowMcp);

        // Once it's on, copying doesn't ask again.
        settings.McpClients[1].IsSelected = true;
        await settings.CopyMcpSetupCommand.ExecuteAsync(null);
        Assert.Null(app.ViewModel.Dialogs.Current);
        Assert.Contains("\"args\": [\"mcp\"]", await app.Get<IClipboardService>().GetTextAsync());
        app.Screenshot("settings-integrations-claude-desktop");

        settings.AllowMcp = false;
        Assert.False(app.Get<ISettingsStore>().Load().AllowMcp);
    }

    [AvaloniaFact]
    public async Task A_development_build_says_it_has_no_command_line_tool()
    {
        var installer = new FakeCommandLineInstaller { BundledPath = null };
        await using var app = await AppHarness.StartAsync(importSamples: false, installer: installer);
        var settings = await OpenIntegrationsAsync(app);

        Assert.False(settings.HasBundledCli);
        Assert.Contains("development build", settings.CliStatus);
    }
}
