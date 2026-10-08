using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Prompuff.App.Platform;

namespace Prompuff.App.Tests;

public class SingleInstanceTests
{
    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "prompuff-instance-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(new string[0], LaunchRequest.Activate)]
    [InlineData(new[] { "--quick-save" }, LaunchRequest.QuickSave)]
    [InlineData(new[] { "quick-save" }, LaunchRequest.QuickSave)]
    [InlineData(new[] { "--QUICK-SAVE" }, LaunchRequest.QuickSave)]
    [InlineData(new[] { "--something-else" }, LaunchRequest.Activate)]
    public void Arguments_choose_what_a_launch_asks_for(string[] args, LaunchRequest expected) =>
        Assert.Equal(expected, LaunchArguments.Parse(args));

    [Fact]
    public async Task A_second_launch_hands_its_request_to_the_first()
    {
        var folder = NewFolder();
        using var first = SingleInstance.Claim(folder);
        Assert.True(first.IsPrimary);
        var received = new BlockingCollection<LaunchRequest>();
        first.Listen();

        using (var second = SingleInstance.Claim(folder))
        {
            Assert.False(second.IsPrimary);
            Assert.True(second.HandOff(LaunchRequest.QuickSave, TimeSpan.FromSeconds(5)));
        }

        // Requests that arrive before the app is ready wait for the handler.
        await Task.Delay(200);
        first.SetHandler(received.Add);
        Assert.True(received.TryTake(out var request, TimeSpan.FromSeconds(5)));
        Assert.Equal(LaunchRequest.QuickSave, request);

        using (var third = SingleInstance.Claim(folder))
        {
            Assert.True(third.HandOff(LaunchRequest.Activate, TimeSpan.FromSeconds(5)));
        }

        Assert.True(received.TryTake(out request, TimeSpan.FromSeconds(5)));
        Assert.Equal(LaunchRequest.Activate, request);
    }

    [Fact]
    public void The_lock_goes_with_the_first_copy()
    {
        var folder = NewFolder();
        var first = SingleInstance.Claim(folder);
        Assert.True(first.IsPrimary);
        first.Dispose();

        using var next = SingleInstance.Claim(folder);
        Assert.True(next.IsPrimary);
    }

    [Fact]
    public void Each_library_folder_has_its_own_copy()
    {
        using var real = SingleInstance.Claim(NewFolder());
        using var development = SingleInstance.Claim(NewFolder());

        Assert.True(real.IsPrimary);
        Assert.True(development.IsPrimary);
        Assert.NotEqual(real.PipeName, development.PipeName);
    }

    [Fact]
    public void A_handoff_with_nobody_listening_gives_up()
    {
        var folder = NewFolder();
        using var first = SingleInstance.Claim(folder);
        using var second = SingleInstance.Claim(folder);

        Assert.False(second.HandOff(LaunchRequest.Activate, TimeSpan.FromMilliseconds(300)));
    }

    [AvaloniaFact]
    public async Task Quick_save_from_outside_opens_with_the_clipboard()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Get<Application.Interfaces.IClipboardService>().SetTextAsync("Summarize {{thread}} in three bullets.");

        await app.ViewModel.QuickSaveFromOutsideAsync();
        await app.SettleAsync();

        Assert.True(app.ViewModel.QuickSave.IsOpen);
        Assert.Equal("Summarize {{thread}} in three bullets.", app.ViewModel.QuickSave.Text);
    }
}
