using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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

            // Capture the pre-attempt error state so VerifyAsync can distinguish
            // a genuine error→healthy transition from a candidate that was already healthy.
            bool wasInError = candidate.Device.HasProblem;

            Logger.Log($"--- Trying: {candidate.Device.InstanceId} (priority={candidate.Priority}, inError={wasInError})");
            Logger.Log($"    Reason : {candidate.Reason}");

            // 4a. Simple enable
            bool ok = await TryEnableAsync(candidate.Device.InstanceId, wasInError, initialHidCount, cancellationToken);
            if (ok)
            {
                Logger.Log($"=== Recovery succeeded (simple enable) with {candidate.Device.InstanceId} ===");
                return true;
            }

            // 4b. Disable → wait 2 s → enable cycle
            Logger.Log("    Simple enable did not recover. Trying disable/enable cycle...");
            ok = await TryDisableEnableCycleAsync(candidate.Device.InstanceId, wasInError, initialHidCount, cancellationToken);
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

    private static async Task<bool> TryEnableAsync(string instanceId, bool wasInError,
        int initialHidCount, CancellationToken ct)
    {
        RunPnpUtil($"/enable-device \"{instanceId}\"");
        await Task.Delay(1000, ct);
        return await VerifyAsync(instanceId, wasInError, initialHidCount);
    }

    private static async Task<bool> TryDisableEnableCycleAsync(string instanceId, bool wasInError,
        int initialHidCount, CancellationToken ct)
    {
        RunPnpUtil($"/disable-device \"{instanceId}\"");
        await Task.Delay(2000, ct);
        RunPnpUtil($"/enable-device \"{instanceId}\"");
        await Task.Delay(1000, ct);
        return await VerifyAsync(instanceId, wasInError, initialHidCount);
    }

    /// <summary>
    /// Checks whether recovery succeeded after a pnputil command.
    /// </summary>
    /// <param name="instanceId">The device instance ID that was acted on.</param>
    /// <param name="targetWasInError">
    ///   Whether the target device had an error status <em>before</em> the command ran.
    ///   Only treat "target is now healthy" as success when this is <c>true</c>; otherwise
    ///   a healthy hub candidate would always short-circuit the loop with a false positive.
    /// </param>
    /// <param name="initialHidCount">Baseline HID input device count before any commands.</param>
    private static async Task<bool> VerifyAsync(string instanceId, bool targetWasInError,
        int initialHidCount)
    {
        // Allow the OS to settle after the pnputil command.
        await Task.Delay(500);

        // Re-enumerate to get fresh state.
        List<DeviceInfo> devices = DeviceEnumerator.GetCandidateDevices();

        // Condition 1: Target device transitioned from error → healthy.
        // Only apply this check when the device was in error before the attempt.
        // Skipping it for healthy candidates (e.g., hubs used as reset points) prevents
        // the loop stopping prematurely on a device that was already healthy.
        if (targetWasInError)
        {
            DeviceInfo? target = devices.Find(
                d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

            if (target is not null && !target.HasProblem)
            {
                Logger.Log($"    Verify OK: target device {instanceId} transitioned from error to healthy");
                return true;
            }
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

    /// <summary>
    /// Runs pnputil with the given arguments, capturing output without risk of deadlock.
    /// Stdout and stderr are read concurrently so a full pipe buffer cannot block the process.
    /// The process is killed if it does not exit within 10 seconds.
    /// </summary>
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

            // Read both streams asynchronously and concurrently to prevent the pipe-buffer
            // deadlock that occurs when ReadToEnd() blocks while the process is also waiting
            // to write to the other stream.
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

            const int TimeoutMs = 10_000;
            bool exited = proc.WaitForExit(TimeoutMs);
            if (!exited)
            {
                Logger.Log($"    WARNING: pnputil did not exit within {TimeoutMs / 1000} s – killing process");
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore kill errors */ }
            }

            // Wait for the output-reading tasks to drain (up to 2 s after process exit/kill).
            bool drained = Task.WaitAll(new[] { stdoutTask, stderrTask }, millisecondsTimeout: 2_000);
            if (!drained)
                Logger.Log("    WARNING: output streams did not drain after process termination");

            string stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            string stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;

            if (!string.IsNullOrWhiteSpace(stdout))
                Logger.Log($"    stdout: {stdout.Trim()}");
            if (!string.IsNullOrWhiteSpace(stderr))
                Logger.Log($"    stderr: {stderr.Trim()}");
            Logger.Log($"    Exit  : {(exited ? proc.ExitCode.ToString() : "timeout/killed")}");
        }
        catch (Exception ex)
        {
            Logger.Log($"    ERROR running pnputil: {ex.Message}");
        }
    }
}
