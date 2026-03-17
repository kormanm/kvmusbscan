using System;
using System.Collections.Generic;
using System.Linq;

namespace KvmUsbScan;

/// <summary>
/// Associates a device with a computed recovery priority (lower = higher priority).
/// </summary>
internal sealed class RankedDevice
{
    public DeviceInfo Device { get; init; } = null!;
    public int Priority { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Ranks PnP devices by their likelihood of being the root cause of USB peripheral failure
/// after a KVM switch event.
///
/// Priority order (lower number = tried first):
///  10 – USB device with error status (most likely root cause)
///  20 – Any device with problem code 43 (device failed after ejection / re-enumeration)
///  30 – Any device with non-zero problem code
///  40 – Generic USB Hub / SuperSpeed USB Hub (healthy but a likely upstream fix point)
///  50 – Any hub by description
///  60 – Unknown USB Device
///
/// HID keyboard / mouse devices are de-prioritized (+100) as they are usually symptoms,
/// not root causes.
/// </summary>
internal static class CandidateRanker
{
    private static readonly string[] HidInputKeywords = ["keyboard", "mouse"];

    /// <summary>
    /// Returns a list of candidate devices sorted by priority (ascending).
    /// Devices that are not candidates are excluded.
    /// </summary>
    internal static List<RankedDevice> Rank(List<DeviceInfo> devices)
    {
        var ranked = new List<RankedDevice>();

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.InstanceId))
                continue;

            int basePriority = ComputeBasePriority(device);
            if (basePriority < 0)
                continue; // Not a candidate

            var desc = device.Description.ToLowerInvariant();
            bool isHidInputDevice =
                HidInputKeywords.Any(kw => desc.Contains(kw)) &&
                string.Equals(device.Class, "HIDClass", StringComparison.OrdinalIgnoreCase);

            // De-prioritize HID input devices (symptoms, not root cause).
            int priority = basePriority + (isHidInputDevice ? 100 : 0);

            ranked.Add(new RankedDevice
            {
                Device = device,
                Priority = priority,
                Reason = BuildReason(device),
            });
        }

        return [.. ranked.OrderBy(r => r.Priority)];
    }

    private static int ComputeBasePriority(DeviceInfo device)
    {
        var desc = device.Description.ToLowerInvariant();
        bool isUsbClass = string.Equals(device.Class, "USB", StringComparison.OrdinalIgnoreCase);

        // Highest priority: USB class device currently in error state.
        if (device.HasProblem && isUsbClass)
            return 10;

        // Problem code 43 – "Device has been stopped" – most common KVM artefact.
        if (device.ProblemCode == 43)
            return 20;

        // Any device with a non-zero problem code.
        if (device.ProblemCode != 0 || device.HasProblem)
            return 30;

        // USB hubs are good intervention points even when they appear healthy.
        if (desc.Contains("generic usb hub") || desc.Contains("superspeed usb hub"))
            return 40;

        if (desc.Contains("usb hub") || desc.Contains(" hub"))
            return 50;

        if (desc.Contains("unknown usb device"))
            return 60;

        return -1; // Not a candidate.
    }

    private static string BuildReason(DeviceInfo device)
    {
        if (device.ProblemCode == 43)
            return $"Code 43 (device stopped) – {device.Description}";
        if (device.ProblemCode != 0)
            return $"ProblemCode={device.ProblemCode} – {device.Description}";
        if (device.HasProblem)
            return $"Status={device.Status} – {device.Description}";

        var desc = device.Description.ToLowerInvariant();
        if (desc.Contains("hub"))
            return $"USB Hub (healthy, used as recovery point) – {device.Description}";
        if (desc.Contains("unknown usb device"))
            return $"Unknown USB Device – {device.Description}";

        return device.Description;
    }
}
