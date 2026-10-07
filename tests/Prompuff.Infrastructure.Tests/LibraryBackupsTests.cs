using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Tests;

public class LibraryBackupsTests
{
    [Fact]
    public async Task A_daily_backup_is_made_once_a_day()
    {
        await using var library = await TestLibrary.CreateAsync();
        var backups = Backups(library);
        await library.PromptService.CreateAsync(new PromptContent("Keep me", null, "body", null));

        Assert.NotNull(await backups.BackUpIfDueAsync());
        library.Time.Advance(TimeSpan.FromHours(3));
        Assert.Null(await backups.BackUpIfDueAsync());
        library.Time.Advance(TimeSpan.FromDays(1));
        Assert.NotNull(await backups.BackUpIfDueAsync());

        var list = backups.List();
        Assert.Equal(2, list.Count);
        Assert.All(list, backup => Assert.Equal(BackupKind.Daily, backup.Kind));
        Assert.True(list[0].CreatedAt > list[1].CreatedAt, "newest first");
    }

    [Fact]
    public async Task Only_the_newest_30_daily_backups_are_kept()
    {
        await using var library = await TestLibrary.CreateAsync();
        var backups = Backups(library);
        await library.Database.BackUpToAsync(Path.Combine(library.Database.BackupDirectory, "prompuff-schema1-20260101-000000.db"));

        for (var day = 0; day < 32; day++)
        {
            await backups.BackUpIfDueAsync();
            library.Time.Advance(TimeSpan.FromDays(1));
        }

        var list = backups.List();
        Assert.Equal(LibraryBackups.DailyBackupsKept, list.Count(backup => backup.Kind == BackupKind.Daily));
        Assert.Single(list, backup => backup.Kind == BackupKind.BeforeUpdate);
        Assert.Equal(library.Time.Now.AddDays(-1), list[0].CreatedAt);
    }

    [Fact]
    public async Task Restoring_brings_the_library_back_and_keeps_the_current_one_aside()
    {
        await using var library = await TestLibrary.CreateAsync();
        var backups = Backups(library);
        var kept = await library.PromptService.CreateAsync(new PromptContent("Kept", null, "body", null));
        var daily = await backups.BackUpIfDueAsync();

        library.Time.Advance(TimeSpan.FromMinutes(5));
        await library.Prompts.DeleteAsync(kept.Id);
        var added = await library.PromptService.CreateAsync(new PromptContent("Added later", null, "body", null));

        await backups.RestoreAsync(daily!);

        Assert.NotNull(await library.Prompts.GetAsync(kept.Id));
        Assert.Null(await library.Prompts.GetAsync(added.Id));

        // The library from before the restore can be restored in turn.
        var aside = Assert.Single(backups.List(), backup => backup.Kind == BackupKind.BeforeRestore);
        await backups.RestoreAsync(aside);
        Assert.Null(await library.Prompts.GetAsync(kept.Id));
        Assert.NotNull(await library.Prompts.GetAsync(added.Id));
    }

    [Fact]
    public async Task A_backup_is_one_file_even_after_it_is_restored()
    {
        await using var library = await TestLibrary.CreateAsync();
        var backups = Backups(library);
        await library.PromptService.CreateAsync(new PromptContent("Keep me", null, "body", null));

        await backups.RestoreAsync((await backups.BackUpIfDueAsync())!);

        Assert.All(Directory.GetFiles(library.Database.BackupDirectory), path => Assert.EndsWith(".db", path));
    }

    [Fact]
    public async Task A_damaged_backup_is_refused_and_the_library_is_left_alone()
    {
        await using var library = await TestLibrary.CreateAsync();
        var backups = Backups(library);
        var prompt = await library.PromptService.CreateAsync(new PromptContent("Still here", null, "body", null));
        Directory.CreateDirectory(library.Database.BackupDirectory);
        var damaged = Path.Combine(library.Database.BackupDirectory, "prompuff-daily-20260930-090000.db");
        await File.WriteAllTextAsync(damaged, "not a database");

        var backup = Assert.Single(backups.List());
        var error = await Assert.ThrowsAsync<LibraryException>(() => backups.RestoreAsync(backup));

        Assert.Contains("hasn't been changed", error.Message);
        Assert.NotNull(await library.Prompts.GetAsync(prompt.Id));
        Assert.DoesNotContain(backups.List(), item => item.Kind == BackupKind.BeforeRestore);
    }

    [Fact]
    public async Task Files_that_are_not_backups_are_ignored()
    {
        await using var library = await TestLibrary.CreateAsync();
        var backups = Backups(library);
        Directory.CreateDirectory(library.Database.BackupDirectory);
        await File.WriteAllTextAsync(Path.Combine(library.Database.BackupDirectory, "notes.txt"), "hi");
        await File.WriteAllTextAsync(Path.Combine(library.Database.BackupDirectory, "prompuff-daily-soon.db"), "hi");

        Assert.Empty(backups.List());
    }

    private static LibraryBackups Backups(TestLibrary library) =>
        new(library.Database, library.Time, NullLogger<LibraryBackups>.Instance);
}
