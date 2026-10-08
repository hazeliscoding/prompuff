using System.Runtime.InteropServices;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Updates;

namespace Prompuff.Infrastructure.Tests;

public class UpdateChannelTests
{
    /// <summary>These names must match the --channel each job in .github/workflows/release.yml packs with.</summary>
    [Theory]
    [InlineData("WINDOWS", Architecture.X64, "win")]
    [InlineData("LINUX", Architecture.X64, "linux")]
    [InlineData("LINUX", Architecture.Arm64, "linux-arm64")]
    [InlineData("OSX", Architecture.Arm64, "osx-arm64")]
    [InlineData("OSX", Architecture.X64, "osx-x64")]
    public void Each_platform_has_a_stable_channel_and_a_beta_one(string os, Architecture architecture, string stable)
    {
        var platform = OSPlatform.Create(os);

        Assert.Equal(stable, VelopackUpdateService.ChannelName(UpdateChannel.Stable, platform, architecture));
        Assert.Equal(stable + "-beta", VelopackUpdateService.ChannelName(UpdateChannel.Beta, platform, architecture));
    }
}
