using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KvmUsbScan;

/// <summary>
/// Orchestrates USB recovery by enumerating PnP devices, ranking candidates,
/// and iteratively applying pnputil enable / disable+enable cycles until
/// recovery is verified or all candidates are exhausted.
/// </summary>
internal sealed class RecoveryEngine
{
    /// <summary>
    /// Attempts USB recovery. Returns <c>true</c> if recovery succeeded.
    /// </summary>
    internal async Task<bool> AttemptRecoveryAsync(CancellationToken cancellationToken = default)
    {
        Logger.Log("=== Recovery started ===");

        // ── Step 1: Enumerate devices ─────────────────────────────────────────
        Logger.Log("Enumerating USB and HID PnP devices via WMI...");
        List<DeviceInfo> devices = DeviceEnumerator.GetCandidateDevices();
        Logger.Log($"Found {devices.Count} relevant devices");

        // ── Step 2: Rank candidates ───────────────────────────────────────────
        List<RankedDevice> candidates = CandidateRanker.Rank(devices);
        Logger.Log($"Ranked {candidates.Count} recovery candidates");

        if (candidates.Count == 0)
        {
            Logger.Log("No candidates found – no recovery action taken");
            return false;
        }

        foreach (var c in candidates)
            Logger.Log($"  [{c.Priority,3}] {c.Device.InstanceId} | {c.Reason}");

        // ── Step 3: Baseline HID input count ─────────────────────────────────
        int initialHidCount = DeviceEnumerator.GetHidInputDeviceCount();
        Logger.Log($"Baseline HID input device count: {initialHidCount}");

        // ── Step 4: Try each candidate in priority order ──────────────────────
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Logger.Log($"--- Trying: {candidate.Device.InstanceId} (priority={candidate.Priority})");
            Logger.Log($"    Reason : {candidate.Reason}");

            // 4a. Simple enable
            bool ok = await TryEnableAsync(candidate.Device.InstanceId, initialHidCount, cancellationToken);
            if (ok)
            {
                Logger.Log($"=== Recovery succeeded (simple enable) with {candidate.Device.InstanceId} ===");
                return true;
            }

            // 4b. Disable → wait 2 s → enable cycle
            Logger.Log("    Simple enable did not recover. Trying disable/enable cycle...");
            ok = await TryDisableEnableCycleAsync(candidate.Device.InstanceId, initialHidCount, cancellationToken);
            if (ok)
            {
                Logger.Log($"=== Recovery succeeded (disable/enable) with {candidate.Device.InstanceId} ===");
                return true;
            }

            Logger.Log($"    Candidate did not fix the issue. Continuing...");
        }

        Logger.Log("=== Recovery exhausted all candidates without success ===");
        return false;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<bool> TryEnableAsync(string instanceId, int initialHidCount,
        CancellationToken ct)
    {
        RunPnpUtil($"/enable-device \"{instanceId}\"");
        await Task.Delay(1000, ct);
        return await VerifyAsync(instanceId, initialHidCount);
    }

    private async Task<bool> TryDisableEnableCycleAsync(string instanceId, int initialHidCount,
        CancellationToken ct)
    {
        RunPnpUtil($"/disable-device \"{instanceId}\"");
        await Task.Delay(2000, ct);
        RunPnpUtil($"/enable-device \"{instanceId}\"");
        await Task.Delay(1000, ct);
        return await VerifyAsync(instanceId, initialHidCount);
    }

    private static async Task<bool> VerifyAsync(string instanceId, int initialHidCount)
    {
        // Allow the OS to settle after the pnputil command.
        await Task.Delay(500);

        // Re-enumerate to get fresh state.
        List<DeviceInfo> devices = DeviceEnumerator.GetCandidateDevices();

        // Condition 1: Target device is no longer in error state.
        DeviceInfo? target = devices.Find(
            d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

        if (target is not null && !target.HasProblem)
        {
            Logger.Log($"    Verify OK: target device {instanceId} is now healthy");
            return true;
        }

        // Condition 2: HID input device count increased (keyboard/mouse re-appeared).
        int currentHidCount = DeviceEnumerator.GetHidInputDeviceCount();
        Logger.Log($"    Verify: HID count {initialHidCount} → {currentHidCount}");
        if (currentHidCount > initialHidCount)
        {
            Logger.Log("    Verify OK: HID input device count increased");
            return true;
        }

        return false;
    }

    private static void RunPnpUtil(string args)
    {
        Logger.Log($"    Exec: pnputil {args}");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start pnputil.exe");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            bool exited = proc.WaitForExit(10_000); // max 10 s

            if (!exited)
                Logger.Log("    WARNING: pnputil did not exit within 10 s – possible hang");

            if (!string.IsNullOrWhiteSpace(stdout))
                Logger.Log($"    stdout: {stdout.Trim()}");
            if (!string.IsNullOrWhiteSpace(stderr))
                Logger.Log($"    stderr: {stderr.Trim()}");
            Logger.Log($"    Exit  : {(exited ? proc.ExitCode.ToString() : "timeout")}");
        }
        catch (Exception ex)
        {
            Logger.Log($"    ERROR running pnputil: {ex.Message}");
        }
    }
}
