[CmdletBinding()]
param(
    [switch]$RemoveUserData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ($fullPath.Length -gt $root.Length) {
        return $fullPath.TrimEnd([char[]]@('\', '/'))
    }

    return $fullPath
}

function Assert-SafeChildPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $candidate = Get-NormalizedPath $Path
    $parentPath = Get-NormalizedPath $Parent
    $prefix = $parentPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing file operation outside '$parentPath': '$candidate'."
    }

    return $candidate
}

function Remove-SafeDirectory {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $safePath = Assert-SafeChildPath -Path $Path -Parent $Parent
    if (Test-Path -LiteralPath $safePath) {
        $item = Get-Item -LiteralPath $safePath -Force
        if (-not $item.PSIsContainer) {
            throw "Refusing recursive delete because '$safePath' is not a directory."
        }

        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

function Test-StartupValueForExecutable {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$ExecutablePath
    )

    if ($null -eq $Value) {
        return $false
    }

    $text = ([string]$Value).Trim()
    if ($text -notmatch '^"(?<path>[^"]+)"$') {
        return $false
    }

    try {
        return [string]::Equals(
            (Get-NormalizedPath $Matches.path),
            (Get-NormalizedPath $ExecutablePath),
            [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Stop-KnownAppProcesses {
    param([Parameter(Mandatory)][string[]]$ExecutablePaths)

    $knownPaths = @($ExecutablePaths | ForEach-Object { Get-NormalizedPath $_ } | Select-Object -Unique)
    $sessionId = (Get-Process -Id $PID).SessionId
    $stoppedIds = @()

    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'WeekCalendarTray.exe'")
    foreach ($process in $processes) {
        if ($process.SessionId -ne $sessionId -or [string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            continue
        }

        $processPath = Get-NormalizedPath $process.ExecutablePath
        $isKnownPath = $knownPaths | Where-Object {
            [string]::Equals($_, $processPath, [StringComparison]::OrdinalIgnoreCase)
        }
        if ($isKnownPath) {
            Stop-Process -Id $process.ProcessId -Force
            $stoppedIds += [int]$process.ProcessId
        }
    }

    if ($stoppedIds.Count -gt 0) {
        Wait-Process -Id $stoppedIds -Timeout 10 -ErrorAction SilentlyContinue
        $remaining = @($stoppedIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($remaining.Count -gt 0) {
            throw "The installed Week Calendar Tray process did not stop. No app files were removed."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or [string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw "LOCALAPPDATA and APPDATA must be available for a per-user uninstall."
}

$installRoot = Join-Path $env:LOCALAPPDATA "WeekCalendarTray"
$installDir = Join-Path $installRoot "App"
$installedExe = Join-Path $installDir "WeekCalendarTray.exe"
$sourceExe = Join-Path $PSScriptRoot "WeekCalendarTray.exe"
$logsDir = Join-Path $installRoot "Logs"
$dataRoot = Get-NormalizedPath $env:APPDATA
$dataDir = Join-Path $dataRoot "WeekCalendarTray"
$shortcutDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $shortcutDir "Week Calendar Tray.lnk"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "WeekCalendarTray"

Assert-SafeChildPath -Path $installDir -Parent $installRoot | Out-Null
Assert-SafeChildPath -Path $logsDir -Parent $installRoot | Out-Null
Assert-SafeChildPath -Path $dataDir -Parent $dataRoot | Out-Null
Assert-SafeChildPath -Path $shortcutPath -Parent $shortcutDir | Out-Null

Stop-KnownAppProcesses -ExecutablePaths @($sourceExe, $installedExe)

$runKey = Get-Item -LiteralPath $runKeyPath -ErrorAction SilentlyContinue
if ($null -ne $runKey) {
    $startupValue = $runKey.GetValue(
        $runValueName,
        $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    if ($null -ne $startupValue) {
        $startupTargetsKnownApp = (Test-StartupValueForExecutable -Value $startupValue -ExecutablePath $installedExe) -or
            (Test-StartupValueForExecutable -Value $startupValue -ExecutablePath $sourceExe)
        if ($startupTargetsKnownApp) {
            Remove-ItemProperty -Path $runKeyPath -Name $runValueName
        } else {
            Write-Warning "The WeekCalendarTray startup entry points to an unexpected command and was left unchanged."
        }
    }
}

if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

Remove-SafeDirectory -Path $installDir -Parent $installRoot

if ($RemoveUserData) {
    Remove-SafeDirectory -Path $dataDir -Parent $dataRoot
    Remove-SafeDirectory -Path $logsDir -Parent $installRoot
}

if (Test-Path -LiteralPath $installRoot -PathType Container) {
    $remainingItems = @(Get-ChildItem -LiteralPath $installRoot -Force)
    if ($remainingItems.Count -eq 0) {
        Remove-Item -LiteralPath $installRoot -Force
    }
}

Write-Host "Week Calendar Tray was uninstalled."
if (-not $RemoveUserData) {
    Write-Host "User settings and cached calendar data were kept at: $dataDir"
    Write-Host "Diagnostic logs were kept at: $logsDir"
}
