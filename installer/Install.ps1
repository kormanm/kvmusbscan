#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install or uninstall KVM USB Recovery.

.DESCRIPTION
    Copies the published application to Program Files, optionally creates a
    Start-Menu shortcut and a Windows startup entry, then launches the app.

.PARAMETER Uninstall
    Remove an existing installation instead of installing.

.PARAMETER InstallDir
    Override the default installation directory.
    Default: $env:ProgramFiles\KvmUsbScan

.EXAMPLE
    # Install (run from the repo root after publishing):
    #   dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\
    .\installer\Install.ps1

.EXAMPLE
    # Uninstall:
    .\installer\Install.ps1 -Uninstall
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Uninstall,
    [string]$InstallDir = "$env:ProgramFiles\KvmUsbScan"
)

$ErrorActionPreference = 'Stop'
$AppName    = 'KvmUsbScan'
$ExeName    = 'KvmUsbScan.exe'
$RunKey     = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$StartMenu  = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\KVM USB Recovery"

function Install {
    $publishDir = Join-Path $PSScriptRoot '..\publish'
    if (-not (Test-Path (Join-Path $publishDir $ExeName))) {
        Write-Error @"
Published output not found at: $publishDir\$ExeName
Please publish first:
  dotnet publish src\KvmUsbScan\KvmUsbScan.csproj -c Release -r win-x64 --self-contained false -o publish\
"@
    }

    Write-Host "Installing to $InstallDir ..." -ForegroundColor Cyan

    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir | Out-Null
    }

    Copy-Item -Path "$publishDir\*" -Destination $InstallDir -Recurse -Force

    # Start-Menu shortcut
    if (-not (Test-Path $StartMenu)) {
        New-Item -ItemType Directory -Path $StartMenu | Out-Null
    }
    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut("$StartMenu\KVM USB Recovery.lnk")
    $shortcut.TargetPath = Join-Path $InstallDir $ExeName
    $shortcut.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wsh) | Out-Null
    Write-Host "  Start-Menu shortcut created" -ForegroundColor Green

    Write-Host "Installation complete." -ForegroundColor Green
    Write-Host ""
    Write-Host "To start automatically with Windows, use the tray icon menu or run:" -ForegroundColor Yellow
    Write-Host "  Set-ItemProperty -Path '$RunKey' -Name $AppName -Value '""$(Join-Path $InstallDir $ExeName)""'" -ForegroundColor Yellow
    Write-Host ""

    # Launch the app
    Start-Process -FilePath (Join-Path $InstallDir $ExeName)
}

function Uninstall {
    Write-Host "Uninstalling KVM USB Recovery..." -ForegroundColor Cyan

    # Stop any running instance
    Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force

    # Remove startup entry
    if (Test-Path $RunKey) {
        Remove-ItemProperty -Path $RunKey -Name $AppName -ErrorAction SilentlyContinue
        Write-Host "  Startup entry removed" -ForegroundColor Green
    }

    # Remove Start-Menu shortcut
    if (Test-Path $StartMenu) {
        Remove-Item -Path $StartMenu -Recurse -Force
        Write-Host "  Start-Menu shortcut removed" -ForegroundColor Green
    }

    # Remove installation directory
    if (Test-Path $InstallDir) {
        Remove-Item -Path $InstallDir -Recurse -Force
        Write-Host "  Installation directory removed: $InstallDir" -ForegroundColor Green
    } else {
        Write-Host "  Installation directory not found (already removed?)" -ForegroundColor Yellow
    }

    Write-Host "Uninstall complete." -ForegroundColor Green
}

if ($Uninstall) {
    Uninstall
} else {
    Install
}
