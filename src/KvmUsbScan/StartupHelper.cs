using System;
using Microsoft.Win32;

namespace KvmUsbScan;

/// <summary>
/// Manages Windows auto-start registration via the current-user Run registry key.
/// Because the app runs elevated, we write to HKCU (no UAC friction for the write).
/// Note: The UAC elevation prompt will still appear on each Windows login because the
/// app manifest requests administrator privileges.
/// </summary>
internal static class StartupHelper
{
    private const string AppName = "KvmUsbScan";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    internal static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupHelper.IsStartupEnabled: {ex.Message}");
            return false;
        }
    }

    internal static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? throw new InvalidOperationException($"Cannot open registry key: {RunKey}");

            if (enabled)
            {
                string exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("Cannot determine executable path");

                key.SetValue(AppName, $"\"{exePath}\"");
                Logger.Log($"Startup enabled: {exePath}");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                Logger.Log("Startup disabled");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupHelper.SetStartupEnabled({enabled}): {ex.Message}");
        }
    }
}
