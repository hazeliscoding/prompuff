using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Prompuff.App.Platform;

/// <summary>What a launch asks of Prompuff: bring the window forward, or open Quick save with the clipboard.</summary>
public enum LaunchRequest
{
    Activate,
    QuickSave,
}

public static class LaunchArguments
{
    /// <summary><c>--quick-save</c> or <c>quick-save</c> opens Quick save; anything else just opens Prompuff.</summary>
    public static LaunchRequest Parse(IEnumerable<string> args) =>
        args.Any(arg => arg.Trim().ToLowerInvariant() is "--quick-save" or "quick-save" or "/quick-save")
            ? LaunchRequest.QuickSave
            : LaunchRequest.Activate;
}

/// <summary>
/// One Prompuff per library. The first copy locks <c>prompuff.lock</c> in the data folder and listens on a local
/// pipe; a later launch finds the lock taken, sends its request down the pipe and exits. The pipe is a named pipe on
/// Windows and a Unix domain socket elsewhere, open only to the same user, and nothing leaves the machine. A
/// development run with its own <c>PROMPUFF_DATA_DIR</c> gets its own lock and pipe.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 64;
    private readonly FileStream? _lock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly Queue<LaunchRequest> _pending = new();
    private Action<LaunchRequest>? _handler;
    private Task? _listening;

    private SingleInstance(bool isPrimary, FileStream? lockFile, string pipeName)
    {
        IsPrimary = isPrimary;
        _lock = lockFile;
        _pipeName = pipeName;
    }

    /// <summary>True for the copy that owns the library; false when another copy is already running.</summary>
    public bool IsPrimary { get; }

    public string PipeName => _pipeName;

    public static SingleInstance Claim(string dataDirectory)
    {
        var pipeName = PipeNameFor(dataDirectory);
        try
        {
            Directory.CreateDirectory(dataDirectory);

            // FileShare.None takes an exclusive lock: a sharing violation on Windows, flock elsewhere. The operating
            // system drops it when the process ends, so a crash never leaves a stale lock behind.
            var lockFile = new FileStream(Path.Combine(dataDirectory, "prompuff.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(isPrimary: true, lockFile, pipeName);
        }
        catch (UnauthorizedAccessException)
        {
            // The data folder can't be used at all. Start anyway, so the library step can say what's wrong.
            return new SingleInstance(isPrimary: true, null, pipeName);
        }
        catch (IOException)
        {
            return new SingleInstance(isPrimary: false, null, pipeName);
        }
    }

    /// <summary>A pipe name unique to the data folder, short enough for a Unix socket path.</summary>
    public static string PipeNameFor(string dataDirectory)
    {
        var path = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!OperatingSystem.IsLinux())
        {
            // Windows and macOS paths ignore case by default.
            path = path.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "prompuff-" + Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>Starts listening for later launches. Requests wait in a queue until <see cref="SetHandler"/> is called.</summary>
    public void Listen()
    {
        if (IsPrimary && _listening is null)
        {
            _listening = Task.Run(() => ListenAsync(_stop.Token));
        }
    }

    /// <summary>Called on a background thread for each request, starting with any that arrived before it was set.</summary>
    public void SetHandler(Action<LaunchRequest> handler)
    {
        List<LaunchRequest> waiting;
        lock (_gate)
        {
            _handler = handler;
            waiting = [.. _pending];
            _pending.Clear();
        }

        foreach (var request in waiting)
        {
            handler(request);
        }
    }

    /// <summary>Hands the request to the running copy. Returns false if it didn't answer in time.</summary>
    public bool HandOff(LaunchRequest request, TimeSpan timeout)
    {
        if (OperatingSystem.IsWindows())
        {
            // This launch came from the user, so it may bring a window forward; pass that right on to the running copy.
            AllowAnyToSetForeground();
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeout);
            var message = Encoding.UTF8.GetBytes(request == LaunchRequest.QuickSave ? "quick-save\n" : "activate\n");
            pipe.Write(message);
            pipe.Flush();
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listening?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Stopping is all that matters here.
        }

        _lock?.Dispose();
        _stop.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);

                var buffer = new byte[MaxMessageBytes];
                var length = 0;
                using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                read.CancelAfter(TimeSpan.FromSeconds(2));
                int count;
                while (length < buffer.Length && (count = await server.ReadAsync(buffer.AsMemory(length), read.Token)) > 0)
                {
                    length += count;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, length).Trim();
                if (message is "quick-save" or "activate")
                {
                    Dispatch(message == "quick-save" ? LaunchRequest.QuickSave : LaunchRequest.Activate);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                // A client that hung up or sent too slowly; wait for the next one.
                await Task.Delay(100, CancellationToken.None);
            }
        }
    }

    private void Dispatch(LaunchRequest request)
    {
        Action<LaunchRequest>? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending.Enqueue(request);
                return;
            }
        }

        handler(request);
    }

    [SupportedOSPlatform("windows")]
    private static void AllowAnyToSetForeground() => _ = AllowSetForegroundWindow(-1);

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
