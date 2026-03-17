# KVM USB Recovery

A lightweight Windows tray application (.NET 8, C#) that **automatically restores USB keyboard and mouse connectivity** after switching back to a laptop connected through a USB-C dock and KVM switch.

---

## Problem

```
Laptop (Windows) → USB-C Dock → KVM → keyboard + mouse
Desktop (Windows) → KVM → keyboard + mouse
```

After switching from the desktop back to the laptop, external monitors reconnect correctly but the USB keyboard and mouse sometimes remain non-functional. Replugging the USB cable between the dock and the KVM restores them. This app automates that recovery.

---

## How It Works

1. **Display detection** – Monitors `SystemEvents.DisplaySettingsChanged`. When the external monitor count increases (laptop regains focus), a debounced recovery is triggered.
2. **Wait** – Waits 2–3 seconds for the OS to settle after the display change.
3. **Device enumeration** – Queries WMI (`Win32_PnPEntity`) for all USB and HID PnP devices and their current state (status, problem code, description).
4. **Candidate ranking** – Builds a dynamic priority list at runtime (never uses a fixed device ID):
   - USB devices with error status (highest priority)
   - Devices with problem code 43 (device stopped after re-enumeration)
   - Any device with a non-zero problem code
   - USB hubs (generic/SuperSpeed) – healthy but useful reset points
   - Unknown USB devices
5. **Recovery** – For each candidate:
   - `pnputil /enable-device "<InstanceId>"`
   - If that doesn't fix it: `pnputil /disable-device` → wait 2 s → `pnputil /enable-device`
   - Verifies after each attempt; stops as soon as recovery is confirmed.
6. **Verification** – Recovery is confirmed when:
   - The target device no longer has an error status, **or**
   - The number of active HID keyboard/mouse devices increased.

---

## Features

- 🖥️ **System tray icon** with context menu
- 🔄 **Automatic recovery** on external display detection
- 🖱️ **Manual recovery** via "Trigger Recovery Now" in the tray menu
- 📋 **Log file** at `%LOCALAPPDATA%\KvmUsbScan\kvmusbscan.log`
- 🚀 **Optional startup** with Windows (toggle in tray menu)
- 🔒 **Administrator elevation** via app manifest (UAC prompt on first launch)
- 🔒 **Single-instance** guard (second launch shows a message and exits)
- No hardcoded device IDs, no learning phase, no kernel drivers

---

## Requirements

- Windows 10 / Windows 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (if not self-contained)

---

## Build

```powershell
# Restore & build
dotnet build KvmUsbScan.sln -c Release

# Publish as single-file executable (framework-dependent)
dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\
```

The output is in `publish\KvmUsbScan.exe`.

---

## Install

### Option A – Inno Setup installer (recommended)

1. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Publish the app:
   ```powershell
   dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\
   ```
3. Compile the installer:
   ```powershell
   iscc installer\setup.iss
   ```
4. Run the generated `installer\Output\KvmUsbScanSetup.exe`.

### Option B – PowerShell script (quick deploy)

```powershell
# Build first
dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\

# Install (requires admin)
.\installer\Install.ps1
```

### Uninstall

```powershell
.\installer\Install.ps1 -Uninstall
```

Or use **Add/Remove Programs** if you used the Inno Setup installer.

---

## Usage

After installation, the app runs in the system tray. Right-click the tray icon for:

| Menu item | Description |
|---|---|
| **Trigger Recovery Now** | Manually start the recovery sequence |
| **Open Log File** | Open `kvmusbscan.log` in the default text editor |
| **Start with Windows** | Toggle auto-start on login |
| **Exit** | Quit the application |

Double-click the tray icon to open the log file.

---

## Logging

Log file: `%LOCALAPPDATA%\KvmUsbScan\kvmusbscan.log`

Each entry is timestamped:
```
[2024-05-01 14:32:01.123] DisplaySettingsChanged: 1 → 3 display(s)
[2024-05-01 14:32:01.124] External display appeared – scheduling recovery (debounce active)
[2024-05-01 14:32:04.125] Debounce elapsed – invoking recovery callback
[2024-05-01 14:32:06.130] === Recovery started ===
[2024-05-01 14:32:06.131] Enumerating USB and HID PnP devices via WMI...
[2024-05-01 14:32:06.500] Found 47 relevant devices
[2024-05-01 14:32:06.501] Ranked 3 recovery candidates
[2024-05-01 14:32:06.502]   [ 10] USB\VID_1234&PID_5678\... | Code 43 (device stopped) – Generic USB Hub
...
[2024-05-01 14:32:08.700] === Recovery succeeded (simple enable) with USB\VID_1234&PID_5678\... ===
```

---

## Elevation / UAC

The application manifest requests `requireAdministrator`. When:
- **Running normally**: Windows shows a UAC prompt once per launch.
- **Starting with Windows via the tray menu**: A UAC prompt appears each time Windows starts. For a seamless experience without a UAC prompt, create a **scheduled task** set to *Run with highest privileges* triggered at logon, instead of using the registry run key.

### Scheduled task (no UAC prompt on startup)

```powershell
$action  = New-ScheduledTaskAction -Execute "$env:ProgramFiles\KvmUsbScan\KvmUsbScan.exe"
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -RunLevel Highest
Register-ScheduledTask -TaskName "KvmUsbScan" -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal -Force
```

---

## Architecture

```
Program.cs          – Entry point, elevation check, single-instance mutex
TrayApp.cs          – ApplicationContext, NotifyIcon, context menu, recovery orchestration
DisplayMonitor.cs   – SystemEvents.DisplaySettingsChanged + debounce timer
DeviceEnumerator.cs – WMI Win32_PnPEntity queries for USB/HID device state
CandidateRanker.cs  – Dynamic priority ranking of recovery candidates
RecoveryEngine.cs   – pnputil enable/disable/enable + verification loop
StartupHelper.cs    – HKCU registry run-key management
Logger.cs           – Timestamped file logger
```

---

## Design Decisions

| Requirement | Implementation |
|---|---|
| No fixed device ID | Rank is computed dynamically from current WMI state on every recovery |
| No baseline / learning | Priority is based purely on real-time PnP status and description patterns |
| Safe – avoid unrelated devices | Candidates must have error status or match known-safe hub descriptions |
| Debounce | 3-second timer reset on each `DisplaySettingsChanged` event |
| Verify before proceeding | Each candidate is verified before trying the next one |

---

## Further Improvements

- **Scheduled task installer** – Register as a Task Scheduler entry instead of registry run key for silent startup.
- **WMI event subscription** – Use `__InstanceModificationEvent` on `Win32_PnPEntity` to react to USB state changes without polling.
- **Custom icon** – Replace the programmatically generated icon with a proper `.ico` resource.
- **Configuration file** – Allow users to tweak the debounce interval and pre-recovery wait via a JSON config.
- **Notification history** – Surface recent recovery attempts in a simple log viewer window.
- **Self-contained publish** – Bundle the .NET 8 runtime for deployment to machines without the runtime installed.
