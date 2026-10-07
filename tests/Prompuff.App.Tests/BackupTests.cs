using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.App.Tests;

public class BackupTests
{
    [AvaloniaFact]
    public async Task Opening_Prompuff_makes_the_days_backup()
    {
        await using var app = await AppHarness.StartAsync();

        var backups = app.Get<LibraryBackups>();
        await app.WaitForAsync(() => backups.List().Count > 0);

        Assert.Equal(BackupKind.Daily, Assert.Single(backups.List()).Kind);
    }

    [AvaloniaFact]
    public async Task A_backup_can_be_restored_from_Settings()
    {
        await using var app = await AppHarness.StartAsync();
        var backups = app.Get<LibraryBackups>();
        await app.WaitForAsync(() => backups.List().Count > 0);
        await app.Get<PromptService>().CreateAsync(new PromptContent("After the backup", null, "body", null));

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        settings.Select(SettingsSection.Storage);
        await app.SettleAsync();
        var row = Assert.Single(settings.Backups);
        Assert.Contains("Daily", row.Detail);
        app.Screenshot("settings-storage-backups");

        var restoring = row.RestoreCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.NotNull(app.ViewModel.Dialogs.Current);
        app.Screenshot("dialog-restore-backup");
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await restoring;
        await app.SettleAsync();

        Assert.Equal(6, (await app.Get<IPromptRepository>().GetCountsAsync()).All);
        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
        Assert.Equal(2, settings.Backups.Count);
        Assert.Contains(settings.Backups, backup => backup.Detail.Contains("Before a restore"));
    }
}
