using Prompuff.Tests;

namespace Prompuff.Infrastructure.Tests;

/// <summary>
/// Fixtures record what released versions wrote and answered, which every later version has to keep reading and
/// answering. Each one is pinned by its SHA-256 (see <see cref="PinnedFixtures"/>), so a change that breaks the 1.0
/// promise can't pass by rewriting the fixture that caught it.
/// </summary>
public class PinnedFixtureTests
{
    [Fact]
    public void Every_pinned_fixture_is_unchanged()
    {
        var changed = PinnedFixtures.Read()
            .Where(pin => !Unchanged(Path.Combine(PinnedFixtures.TestsFolder, pin.Key), pin.Value))
            .Select(pin => pin.Key)
            .ToList();

        Assert.True(changed.Count == 0,
            $"These fixtures record a release and must not change: {string.Join(", ", changed)}. Restore them from git. "
            + "When a release stores or answers something new, add a new fixture beside them.");
    }

    [Fact]
    public void Every_fixture_is_pinned()
    {
        var pinned = PinnedFixtures.Read();
        var unpinned = Directory.GetDirectories(PinnedFixtures.TestsFolder)
            .Select(project => Path.Combine(project, "Fixtures"))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Select(PinnedFixtures.Key)
            .Where(key => !pinned.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unpinned.Count == 0,
            $"These fixtures aren't pinned in tests/{PinnedFixtures.ManifestName}: {string.Join(", ", unpinned)}. "
            + "The test that writes a fixture pins it; a hand-made one needs its line added.");
    }

    private static bool Unchanged(string path, string hash) => File.Exists(path) && PinnedFixtures.Hash(path) == hash;
}
