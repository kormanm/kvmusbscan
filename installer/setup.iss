; ============================================================
; KVM USB Recovery – Inno Setup installer script
; Requires: Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
; Build command (from repo root, after publishing the app):
;   dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\
;   iscc installer\setup.iss
; ============================================================

#define MyAppName      "KVM USB Recovery"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "kormanm"
#define MyAppExeName   "KvmUsbScan.exe"

[Setup]
; AppId uses {{ to produce a literal { so the resulting value is the standard {GUID} format.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567891}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputBaseFilename=KvmUsbScanSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Require admin rights so pnputil works
PrivilegesRequired=admin
; Windows 10 / 11 minimum
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry"; Description: "Start automatically with Windows"; GroupDescription: "Startup:"

[Files]
; Publish the app to publish\ before running this script:
;   dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Include any additional runtime DLLs produced by publish
Source: "..\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\*.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the startup registry entry written by the app itself
Filename: "reg.exe"; Parameters: "delete ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"" /v KvmUsbScan /f"; Flags: runhidden; StatusMsg: "Removing startup entry..."
