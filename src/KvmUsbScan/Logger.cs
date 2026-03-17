using System;
using System.IO;

namespace KvmUsbScan;

/// <summary>
/// Provides simple timestamped file logging to %LOCALAPPDATA%\KvmUsbScan\kvmusbscan.log.
/// Thread-safe via a lock object.
/// </summary>
internal static class Logger
{
    private static readonly string LogPath;
    private static readonly object LockObj = new();

    static Logger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "KvmUsbScan");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "kvmusbscan.log");
    }

    /// <summary>Returns the full path to the log file.</summary>
    internal static string GetLogPath() => LogPath;

    /// <summary>Appends a timestamped message to the log file.</summary>
    internal static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        lock (LockObj)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // Best-effort logging; never crash the app due to a log write failure.
            }
        }
    }

    /// <summary>Formats and logs a message.</summary>
    internal static void Log(string format, params object[] args) =>
        Log(string.Format(format, args));
}
