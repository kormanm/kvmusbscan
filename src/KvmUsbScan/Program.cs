using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace KvmUsbScan;

internal static class Program
{
    // Keep the mutex alive for the entire process lifetime.
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main()
    {
        // ── Single-instance guard ─────────────────────────────────────────────
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: "Global\\KvmUsbScan_SingleInstance",
            createdNew: out bool isNewInstance);

        if (!isNewInstance)
        {
            MessageBox.Show(
                "KVM USB Recovery is already running.\nCheck the system tray.",
                "KVM USB Recovery",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _singleInstanceMutex.Dispose();
            return;
        }

        // ── Elevation check ───────────────────────────────────────────────────
        // The app.manifest requests 'requireAdministrator', so the OS handles elevation
        // before Main() is called. This code path is reached only if the manifest did
        // not take effect (e.g., during development without the manifest).
        if (!IsRunningAsAdministrator())
        {
            TryRelaunchElevated();
            ReleaseMutexAndExit();
            return;
        }

        // ── Start WinForms message loop ───────────────────────────────────────
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var trayApp = new TrayApp();
        Application.Run(trayApp);

        ReleaseMutexAndExit();
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryRelaunchElevated()
    {
        try
        {
            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot determine executable path");

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "KVM USB Recovery requires administrator privileges to control USB devices.\n\n" +
                $"Error: {ex.Message}",
                "Elevation Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void ReleaseMutexAndExit()
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* already released */ }
        _singleInstanceMutex?.Dispose();
    }
}
