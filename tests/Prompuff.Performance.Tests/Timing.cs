using System.Diagnostics;

namespace Prompuff.Performance.Tests;

internal static class Timing
{
    /// <summary>
    /// Runs <paramref name="action"/> once to warm up, then <paramref name="runs"/> more times, and returns the median,
    /// so a single slow run on a busy machine doesn't decide the result.
    /// </summary>
    public static async Task<TimeSpan> MedianAsync(Func<Task> action, int runs = 9)
    {
        await action();
        var times = new List<TimeSpan>(runs);
        for (var i = 0; i < runs; i++)
        {
            var watch = Stopwatch.StartNew();
            await action();
            times.Add(watch.Elapsed);
        }

        times.Sort();
        return times[runs / 2];
    }

    /// <summary>Writes a measurement to the test output, where <c>dotnet test --logger "console;verbosity=detailed"</c> shows it.</summary>
    public static void Report(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);
}
