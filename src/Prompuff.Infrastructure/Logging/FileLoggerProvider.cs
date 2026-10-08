using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Prompuff.Infrastructure.Logging;

/// <summary>
/// Writes one log file per day (<c>prompuff-yyyyMMdd.log</c>) and deletes files older than the retention period.
/// Messages carry IDs and counts only; prompt content is never logged. The command-line tool writes its own files
/// (<c>prompuff-cli-yyyyMMdd.log</c>), and every line is written at the current end of the file, so several processes
/// can share a log without overwriting each other.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly LogLevel _minimumLevel;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _currentDay;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14, string filePrefix = "prompuff")
    {
        _directory = directory;
        _filePrefix = filePrefix;
        _minimumLevel = minimumLevel;
        try
        {
            Directory.CreateDirectory(directory);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var file in Directory.EnumerateFiles(directory, "prompuff-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never stop the app from starting.
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortCategory(categoryName));

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = DateTime.UtcNow;
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(Abbreviate(level)).Append("] ")
            .Append(category).Append(": ")
            .Append(message);
        if (exception is not null)
        {
            line.Append('\n').Append(exception);
        }

        lock (_gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(now);
                if (_writer is null || today != _currentDay)
                {
                    _writer?.Dispose();
                    var path = Path.Combine(_directory, $"{_filePrefix}-{now:yyyyMMdd}.log");
                    _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
                    {
                        AutoFlush = true,
                        NewLine = "\n",
                    };
                    _currentDay = today;
                }

                _writer.BaseStream.Seek(0, SeekOrigin.End);
                _writer.WriteLine(line.ToString());
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
            {
                _writer = null;
            }
        }
    }

    private static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}
