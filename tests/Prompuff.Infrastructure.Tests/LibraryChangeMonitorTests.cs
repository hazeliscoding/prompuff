using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Tests;

public class LibraryChangeMonitorTests
{
    [Fact]
    public async Task Notices_a_write_from_another_connection_once()
    {
        await using var library = await TestLibrary.CreateAsync();
        await using var monitor = new LibraryChangeMonitor(library.Database);

        Assert.False(await monitor.CheckAsync());
        Assert.False(await monitor.CheckAsync());

        await library.PromptService.CreateAsync(new PromptContent("Saved elsewhere", null, "body", null));
        Assert.True(await monitor.CheckAsync());
        Assert.False(await monitor.CheckAsync());

        await library.PromptService.CreateAsync(new PromptContent("Seen already", null, "body", null));
        await monitor.ResetAsync();
        Assert.False(await monitor.CheckAsync());
    }
}
