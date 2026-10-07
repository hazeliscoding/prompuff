using Avalonia.Headless.XUnit;
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
}
