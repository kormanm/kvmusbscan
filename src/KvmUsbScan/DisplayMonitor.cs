using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KvmUsbScan;

/// <summary>
/// Monitors display topology changes via <see cref="SystemEvents.DisplaySettingsChanged"/>
/// and fires a callback when the monitor count increases (external display connected).
///
/// A debounce timer prevents multiple rapid events from triggering multiple recoveries.
/// </summary>
internal sealed class DisplayMonitor : IDisposable
{
    private readonly Action _onExternalDisplayAppeared;
    private int _lastDisplayCount;
    private System.Threading.Timer? _debounceTimer;
    private bool _disposed;

    // Debounce: wait 3 s after the last display-change event before firing the callback.
    private const int DebounceMs = 3_000;

    internal DisplayMonitor(Action onExternalDisplayAppeared)
    {
        _onExternalDisplayAppeared = onExternalDisplayAppeared;
        _lastDisplayCount = Screen.AllScreens.Length;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Logger.Log($"DisplayMonitor started. Initial display count: {_lastDisplayCount}");
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        int newCount = Screen.AllScreens.Length;
        Logger.Log($"DisplaySettingsChanged: {_lastDisplayCount} → {newCount} display(s)");

        if (newCount > _lastDisplayCount)
        {
            Logger.Log("External display appeared – scheduling recovery (debounce active)");
            ScheduleRecovery();
        }

        _lastDisplayCount = newCount;
    }

    private void ScheduleRecovery()
    {
        // Reset the debounce timer on every event so that a burst of changes results
        // in only one recovery attempt, triggered after the burst settles.
        Interlocked.Exchange(ref _debounceTimer, null)?.Dispose();

        _debounceTimer = new System.Threading.Timer(_ =>
        {
            Interlocked.Exchange(ref _debounceTimer, null)?.Dispose();
            Logger.Log("Debounce elapsed – invoking recovery callback");
            _onExternalDisplayAppeared();
        }, null, DebounceMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Interlocked.Exchange(ref _debounceTimer, null)?.Dispose();
    }
}
