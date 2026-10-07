using Prompuff.Application.DTOs;

namespace Prompuff.Application.Services;

/// <summary>A line-based diff using the longest common subsequence. Prompts are small, so this stays simple.</summary>
public static class LineDiff
{
    /// <summary>Above this many cells the middle section is shown as a block replacement instead.</summary>
    private const long MaxTableCells = 4_000_000;

    public static IReadOnlyList<DiffLine> Compute(string? oldText, string? newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);
        var result = new List<DiffLine>(Math.Max(oldLines.Length, newLines.Length));

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
               && oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix])
        {
            suffix++;
        }

        for (var i = 0; i < prefix; i++)
        {
            result.Add(new DiffLine(DiffLineKind.Unchanged, oldLines[i], i + 1, i + 1));
        }

        DiffMiddle(oldLines, newLines, prefix, oldLines.Length - suffix, prefix, newLines.Length - suffix, result);

        for (var i = 0; i < suffix; i++)
        {
            var oldIndex = oldLines.Length - suffix + i;
            var newIndex = newLines.Length - suffix + i;
            result.Add(new DiffLine(DiffLineKind.Unchanged, oldLines[oldIndex], oldIndex + 1, newIndex + 1));
        }

        return result;
    }

    public static (int Added, int Removed) CountChanges(string? oldText, string? newText)
    {
        var lines = Compute(oldText, newText);
        return (lines.Count(line => line.Kind == DiffLineKind.Added), lines.Count(line => line.Kind == DiffLineKind.Removed));
    }

    private static void DiffMiddle(string[] a, string[] b, int aStart, int aEnd, int bStart, int bEnd, List<DiffLine> result)
    {
        var n = aEnd - aStart;
        var m = bEnd - bStart;

        if ((long)n * m > MaxTableCells)
        {
            for (var i = aStart; i < aEnd; i++)
            {
                result.Add(new DiffLine(DiffLineKind.Removed, a[i], i + 1, null));
            }

            for (var j = bStart; j < bEnd; j++)
            {
                result.Add(new DiffLine(DiffLineKind.Added, b[j], null, j + 1));
            }

            return;
        }

        // lcs[i, j] = length of the LCS of a[aStart + i..aEnd] and b[bStart + j..bEnd]
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = a[aStart + i] == b[bStart + j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[aStart + x] == b[bStart + y])
            {
                result.Add(new DiffLine(DiffLineKind.Unchanged, a[aStart + x], aStart + x + 1, bStart + y + 1));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine(DiffLineKind.Removed, a[aStart + x], aStart + x + 1, null));
                x++;
            }
            else
            {
                result.Add(new DiffLine(DiffLineKind.Added, b[bStart + y], null, bStart + y + 1));
                y++;
            }
        }

        for (; x < n; x++)
        {
            result.Add(new DiffLine(DiffLineKind.Removed, a[aStart + x], aStart + x + 1, null));
        }

        for (; y < m; y++)
        {
            result.Add(new DiffLine(DiffLineKind.Added, b[bStart + y], null, bStart + y + 1));
        }
    }

    private static string[] SplitLines(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Split('\n');
}
