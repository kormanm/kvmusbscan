using System;
using System.Collections.Generic;
using System.Management;

namespace KvmUsbScan;

/// <summary>
/// Represents a single PnP device with its state information.
/// </summary>
internal sealed class DeviceInfo
{
    public string InstanceId { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ProblemCode { get; set; }
    public string Description { get; set; } = string.Empty;

    public bool HasProblem =>
        !string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase) || ProblemCode != 0;
}

/// <summary>
/// Enumerates PnP devices via WMI (Win32_PnPEntity).
/// </summary>
internal static class DeviceEnumerator
{
    /// <summary>
    /// Returns USB and HIDClass devices plus any device with a non-zero ConfigManagerErrorCode.
    /// No class is hard-coded for device IDs; results are dynamic every call.
    /// </summary>
    internal static List<DeviceInfo> GetCandidateDevices()
    {
        var devices = new List<DeviceInfo>();

        // Retrieve USB + HID devices, and any device currently flagged with an error.
        const string query =
            "SELECT DeviceID, PNPClass, Status, ConfigManagerErrorCode, Name " +
            "FROM Win32_PnPEntity " +
            "WHERE PNPClass = 'USB' OR PNPClass = 'HIDClass' OR ConfigManagerErrorCode != 0";

        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var device = new DeviceInfo
                    {
                        InstanceId = obj["DeviceID"]?.ToString() ?? string.Empty,
                        Class = obj["PNPClass"]?.ToString() ?? string.Empty,
                        Status = obj["Status"]?.ToString() ?? string.Empty,
                        ProblemCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0),
                        Description = obj["Name"]?.ToString() ?? string.Empty,
                    };
                    if (!string.IsNullOrWhiteSpace(device.InstanceId))
                        devices.Add(device);
                }
                catch
                {
                    // Skip individual faulty WMI entries.
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"DeviceEnumerator: WMI query failed – {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Returns the count of working HID keyboard / mouse devices.
    /// Used to detect whether recovery restored peripherals.
    /// </summary>
    internal static int GetHidInputDeviceCount()
    {
        int count = 0;

        const string query =
            "SELECT Name, PNPClass " +
            "FROM Win32_PnPEntity " +
            "WHERE (PNPClass = 'HIDClass' OR PNPClass = 'Keyboard' OR PNPClass = 'Mouse') " +
            "AND Status = 'OK' AND ConfigManagerErrorCode = 0";

        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var cls = (obj["PNPClass"]?.ToString() ?? string.Empty).ToLowerInvariant();
                    var name = (obj["Name"]?.ToString() ?? string.Empty).ToLowerInvariant();

                    if (cls is "keyboard" or "mouse" ||
                        name.Contains("keyboard") || name.Contains("mouse"))
                    {
                        count++;
                    }
                }
                catch { /* skip */ }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"DeviceEnumerator: HID count query failed – {ex.Message}");
        }

        return count;
    }
}
