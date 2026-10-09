using System.Security.Cryptography;

namespace Prompuff.Tests;

/// <summary>
/// A fixture records what a release wrote or answered, so every later version can be held to it. Each file in a test
/// project's <c>Fixtures</c> folder is pinned by its SHA-256 in <c>tests/pinned-fixtures.sha256</c> as it's written,
/// and <c>PinnedFixtureTests</c> fails when one changes or goes missing. A pinned fixture is never rewritten: a release
/// that stores or answers something new adds a fixture beside the old ones.
/// </summary>
internal static class PinnedFixtures
{
    public const string ManifestName = "pinned-fixtures.sha256";

    /// <summary>The <c>tests</c> folder of this checkout, found above the test binaries.</summary>
    public static string TestsFolder { get; } = FindTestsFolder();

    public static string ManifestPath => Path.Combine(TestsFolder, ManifestName);

    /// <summary>
    /// Each pinned file's hash, by its path under <c>tests</c> with forward slashes. The lines are in
    /// <c>sha256sum</c>'s format, so <c>sha256sum -c pinned-fixtures.sha256</c> checks them from a shell too.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read() =>
        File.ReadAllLines(ManifestPath)
            .Where(line => line.Length > 0)
            .Select(line => line.Split("  ", 2))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);

    public static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public static string Key(string path) => Path.GetRelativePath(TestsFolder, path).Replace('\\', '/');

    /// <summary>Stops before a pinned fixture would be overwritten.</summary>
    public static void EnsureUnpinned(string path)
    {
        if (Read().ContainsKey(Key(path)))
        {
            throw new InvalidOperationException(
                $"{Key(path)} is pinned in tests/{ManifestName}: it records a release, so it's never rewritten. Add a new fixture instead.");
        }
    }

    /// <summary>Pins a fixture that was just written.</summary>
    public static void Pin(string path)
    {
        EnsureUnpinned(path);
        File.AppendAllText(ManifestPath, $"{Hash(path)}  {Key(path)}\n");
    }

    private static string FindTestsFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, ManifestName)))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"There's no {ManifestName} above {AppContext.BaseDirectory}.");
    }
}
