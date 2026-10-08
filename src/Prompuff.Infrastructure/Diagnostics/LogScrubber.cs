using System.Text;
using System.Text.RegularExpressions;

namespace Prompuff.Infrastructure.Diagnostics;

/// <summary>
/// A second look at log lines before they go into a diagnostic report. Prompuff never logs prompt text, so nothing
/// here should ever fire; it's there in case a future message or exception slips. A line is left out when it isn't
/// shaped like log output (such as the rest of a multi-line message), when it holds a <c>{{variable}}</c>, or when
/// <see cref="LeaveOutLinesHolding"/> finds library text in it. Text in “curly quotes”, which is how Prompuff quotes
/// names in its messages, is blanked out.
/// </summary>
internal sealed partial class LogScrubber
{
    /// <summary>Long text matches on any run of this many words, so a fragment of a body is enough.</summary>
    public const int PhraseWords = 5;

    /// <summary>Shorter text, such as a remembered value of "22", says too little to leave a line out for.</summary>
    public const int ShortestText = 3;

    private readonly List<Line>[] _logs;
    private readonly Dictionary<string, List<Line>> _phrases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Line>>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly List<int> _starts = [];

    /// <param name="logs">Each log's lines, oldest first.</param>
    public LogScrubber(params IReadOnlyList<string>[] logs)
    {
        _lookup = _phrases.GetAlternateLookup<ReadOnlySpan<char>>();
        _logs = new List<Line>[logs.Length];
        for (var log = 0; log < logs.Length; log++)
        {
            _logs[log] = [];
            foreach (var text in logs[log])
            {
                var line = Shape(text);
                _logs[log].Add(line);
                if (line.Kept)
                {
                    Index(line);
                }
            }
        }
    }

    /// <summary>The lines of one log that are safe to share, in order.</summary>
    public IReadOnlyList<string> Kept(int log) => _logs[log].Where(line => line.Kept).Select(line => line.Text).ToList();

    public int LeftOut(int log) => _logs[log].Count(line => !line.Kept);

    /// <summary>
    /// Leaves out every line that holds this text from the library: all of it when it's short, such as a title, or any
    /// <see cref="PhraseWords"/> words in a row from it when it's long, such as a body. Case, punctuation and accents
    /// aside, so a title also matches its file name in an exported zip.
    /// </summary>
    public void LeaveOutLinesHolding(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var words = Words(text);
        if (words.Length < ShortestText)
        {
            return;
        }

        _starts.Clear();
        _starts.Add(0);
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i] == ' ')
            {
                _starts.Add(i + 1);
            }
        }

        if (_starts.Count <= PhraseWords)
        {
            LeaveOut(words);
            return;
        }

        for (var word = 0; word + PhraseWords <= _starts.Count; word++)
        {
            var end = word + PhraseWords < _starts.Count ? _starts[word + PhraseWords] - 1 : words.Length;
            LeaveOut(words.AsSpan(_starts[word], end - _starts[word]));
        }
    }

    /// <summary>Lowercase letters and digits, with every other run of characters as one space.</summary>
    internal static string Words(string text)
    {
        var words = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                words.Append(char.ToLowerInvariant(ch));
            }
            else if (words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }
        }

        return words.ToString().TrimEnd();
    }

    private void LeaveOut(ReadOnlySpan<char> phrase)
    {
        if (_lookup.TryGetValue(phrase, out var lines))
        {
            foreach (var line in lines)
            {
                line.Kept = false;
            }
        }
    }

    /// <summary>Keeps entries, exception headers and stack frames, with quoted names blanked out. Leaves out anything else.</summary>
    private static Line Shape(string text)
    {
        if (Variable().IsMatch(text))
        {
            return Line.LeftOut(text);
        }

        if (StackFrame().IsMatch(text) || EndOfTrace().IsMatch(text))
        {
            return new Line(text, text);
        }

        var match = Entry().Match(text);
        if (!match.Success)
        {
            match = ExceptionHeader().Match(text);
        }

        if (!match.Success)
        {
            return Line.LeftOut(text);
        }

        var message = match.Groups["message"];
        if (!message.Success)
        {
            return new Line(text, string.Empty);
        }

        var blanked = Quoted().Replace(message.Value, "“…”");
        return new Line(text[..message.Index] + blanked, blanked);
    }

    /// <summary>Every run of one to <see cref="PhraseWords"/> words in the line's message, or in the whole of a stack frame.</summary>
    private void Index(Line line)
    {
        var words = Words(line.Searchable).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var count = 1; count <= PhraseWords; count++)
        {
            for (var first = 0; first + count <= words.Length; first++)
            {
                var phrase = string.Join(' ', words, first, count);
                if (!_phrases.TryGetValue(phrase, out var lines))
                {
                    _phrases[phrase] = lines = [];
                }

                lines.Add(line);
            }
        }
    }

    // 2026-10-08 14:03:07.123 [INF] SettingsViewModel: Theme nord chosen
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[[A-Z-]{3}\] [^\s:]+: (?<message>.*)$")]
    private static partial Regex Entry();

    // System.IO.IOException: …, Microsoft.Data.Sqlite.SqliteException (0x80004005): …, and " ---> " before an inner one.
    [GeneratedRegex(@"^(\s*---> )?[A-Za-z_][\w.`+]*(Exception|Error)( \(0x[0-9A-Fa-f]+\))?(: (?<message>.*))?$")]
    private static partial Regex ExceptionHeader();

    // "   at Prompuff.App.ViewModels.SettingsViewModel.Save(Guid id) in /src/SettingsViewModel.cs:line 42"
    [GeneratedRegex(@"^\s+at [^\s(]+\(.*\)( in .+:line \d+)?$")]
    private static partial Regex StackFrame();

    [GeneratedRegex(@"^\s*--- End of [a-z ]+ ---$")]
    private static partial Regex EndOfTrace();

    [GeneratedRegex("“[^”]*”")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\{\{\s*[A-Za-z_][A-Za-z0-9_]*\s*\}\}")]
    private static partial Regex Variable();

    /// <summary>A line as it goes in the report, and the part that could hold library text: the message, or all of a stack frame.</summary>
    private sealed class Line(string text, string searchable)
    {
        public string Text { get; } = text;
        public string Searchable { get; } = searchable;
        public bool Kept { get; set; } = true;

        public static Line LeftOut(string text) => new(text, string.Empty) { Kept = false };
    }
}
