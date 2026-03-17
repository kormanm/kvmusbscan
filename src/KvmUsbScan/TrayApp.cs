using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KvmUsbScan;

/// <summary>
/// Windows tray application context. Manages the system tray icon, context menu,
/// display monitoring, and recovery orchestration.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly RecoveryEngine _recoveryEngine = new();
    private DisplayMonitor? _displayMonitor;
    private SynchronizationContext? _uiContext;
    private CancellationTokenSource? _recoveryCts;
    private volatile bool _isRecovering;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    internal TrayApp()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "KVM USB Recovery",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => OpenLog();

        // Defer display monitor initialisation until after the message loop is
        // running so that SynchronizationContext.Current is fully established.
        var initTimer = new System.Windows.Forms.Timer { Interval = 50 };
        initTimer.Tick += (s, _) =>
        {
            ((System.Windows.Forms.Timer)s!).Stop();
            ((System.Windows.Forms.Timer)s!).Dispose();

            _uiContext = SynchronizationContext.Current;
            _displayMonitor = new DisplayMonitor(OnExternalDisplayAppeared);
            Logger.Log("KvmUsbScan tray application ready");
            ShowBalloon("KVM USB Recovery", "Monitoring for display changes.", ToolTipIcon.Info);
        };
        initTimer.Start();
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var triggerItem = new ToolStripMenuItem("Trigger Recovery Now");
        triggerItem.Click += (_, _) => TriggerManualRecovery();
        menu.Items.Add(triggerItem);

        menu.Items.Add(new ToolStripSeparator());

        var openLogItem = new ToolStripMenuItem("Open Log File");
        openLogItem.Click += (_, _) => OpenLog();
        menu.Items.Add(openLogItem);

        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupHelper.IsStartupEnabled(),
            CheckOnClick = true,
        };
        startupItem.CheckedChanged += (s, _) =>
        {
            if (s is ToolStripMenuItem item)
                StartupHelper.SetStartupEnabled(item.Checked);
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        return menu;
    }

    // ── Recovery orchestration ────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="DisplayMonitor"/> on a ThreadPool thread when an external
    /// display is detected. Waits 2 s before starting recovery to let the OS settle.
    /// </summary>
    private void OnExternalDisplayAppeared()
    {
        if (_isRecovering)
        {
            Logger.Log("Display event received but recovery already in progress – skipped");
            return;
        }

        Logger.Log("External display detected. Waiting 2 s before recovery...");
        ShowBalloon("KVM USB Recovery",
            "External display detected. USB recovery will start shortly...",
            ToolTipIcon.Info);

        Task.Run(async () =>
        {
            await Task.Delay(2_000);
            await RunRecoveryAsync();
        });
    }

    private void TriggerManualRecovery()
    {
        if (_isRecovering)
        {
            MessageBox.Show(
                "Recovery is already in progress.",
                "KVM USB Recovery",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Logger.Log("Manual recovery triggered via tray menu");
        Task.Run(RunRecoveryAsync);
    }

    private async Task RunRecoveryAsync()
    {
        if (_isRecovering) return;
        _isRecovering = true;

        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _recoveryCts, cts);
        old?.Cancel();
        old?.Dispose();

        try
        {
            bool success = await _recoveryEngine.AttemptRecoveryAsync(cts.Token);

            string title = success ? "Recovery Successful" : "Recovery Complete";
            string message = success
                ? "USB keyboard/mouse should be working again."
                : "No broken devices found, or recovery could not fix the issue.";
            ToolTipIcon icon = success ? ToolTipIcon.Info : ToolTipIcon.Warning;

            Logger.Log($"Recovery finished – success={success}");
            ShowBalloon(title, message, icon);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Recovery cancelled");
        }
        catch (Exception ex)
        {
            Logger.Log($"Unhandled recovery error: {ex}");
            ShowBalloon("Recovery Error", $"An error occurred: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _isRecovering = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        void Show() => _notifyIcon.ShowBalloonTip(5_000, title, message, icon);

        if (_uiContext is not null)
            _uiContext.Post(_ => Show(), null);
        else
            Show(); // Still in constructor phase, called from UI thread.
    }

    private static void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Logger.GetLogPath(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open log file:\n{ex.Message}",
                "KVM USB Recovery",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExitApp()
    {
        Logger.Log("Application exiting");
        _recoveryCts?.Cancel();
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    // ── Icon creation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Programmatically creates a 16×16 tray icon: a blue circle with "U" for USB.
    /// </summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(Color.FromArgb(30, 144, 255)); // DodgerBlue
            g.FillEllipse(bgBrush, 1, 1, 13, 13);

            using var font = new Font("Arial", 7f, FontStyle.Bold, GraphicsUnit.Point);
            using var fgBrush = new SolidBrush(Color.White);
            g.DrawString("U", font, fgBrush, 3f, 2f);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            // Clone so the Icon owns its own handle, then destroy the GDI+ HICON.
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _displayMonitor?.Dispose();
            _recoveryCts?.Cancel();
            _recoveryCts?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
