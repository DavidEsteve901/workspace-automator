using System.Collections.Concurrent;
using System.IO;

namespace WorkspaceLauncher.Core.Utils;

/// <summary>
/// Simple thread-safe logger that buffers messages and notifies listeners.
/// Used to display backend activity in the Frontend Log Console.
/// </summary>
public static class Logger
{
    public static event Action<LogEntry>? OnLog;

    private static readonly ConcurrentQueue<LogEntry> _history = new();
    private const int MaxHistory = 500;

    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "debug.log");

    public static void Log(string message, string level = "info")
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var entry = new LogEntry 
        { 
            Timestamp = DateTime.Now.ToString("HH:mm:ss"), 
            Message = message, 
            Level = level 
        };

        _history.Enqueue(entry);
        while (_history.Count > MaxHistory) _history.TryDequeue(out _);

        string logLine = $"[{timestamp}] [{level.ToUpper()}] {message}";
        Console.WriteLine(logLine);
        
        try
        {
            File.AppendAllText(LogFile, logLine + Environment.NewLine);
        }
        catch { /* Best effort logging */ }

        OnLog?.Invoke(entry);
    }

    public static void Info(string msg) => Log(msg, "info");
    public static void Success(string msg) => Log(msg, "success");
    public static void Warn(string msg) => Log(msg, "warn");
    public static void Error(string msg) => Log(msg, "error");

    public static IEnumerable<LogEntry> GetHistory() => _history.ToArray();
}

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Message { get; set; } = "";
    public string Level { get; set; } = "info"; // info | success | warn | error
}


