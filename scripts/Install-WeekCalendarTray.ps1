[CmdletBinding()]
param(
    [switch]$NoStartup,
    [switch]$NoLaunch,
    [switch]$PreserveStartup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($NoStartup -and $PreserveStartup) {
    throw "-NoStartup and -PreserveStartup cannot be used together."
}

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

function Move-SafeDirectory {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$Parent
    )

    $safeSource = Assert-SafeChildPath -Path $Source -Parent $Parent
    $safeDestination = Assert-SafeChildPath -Path $Destination -Parent $Parent
    if (-not (Test-Path -LiteralPath $safeSource -PathType Container)) {
        throw "Move source directory does not exist: '$safeSource'."
    }
    if (Test-Path -LiteralPath $safeDestination) {
        throw "Move destination already exists: '$safeDestination'."
    }

    Move-Item -LiteralPath $safeSource -Destination $safeDestination
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
            throw "Week Calendar Tray did not stop cleanly; installation was not replaced."
        }
    }
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA) -or [string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw "LOCALAPPDATA and APPDATA must be available for a per-user installation."
}

$sourceDir = Get-NormalizedPath $PSScriptRoot
$sourceExe = Join-Path $sourceDir "WeekCalendarTray.exe"
$installRoot = Join-Path $env:LOCALAPPDATA "WeekCalendarTray"
$installDir = Join-Path $installRoot "App"
$installedExe = Join-Path $installDir "WeekCalendarTray.exe"
$operationId = [Guid]::NewGuid().ToString("N")
$stageDir = Join-Path $installRoot ".install-stage-$operationId"
$backupDir = Join-Path $installRoot ".install-backup-$operationId"
$shortcutDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $shortcutDir "Week Calendar Tray.lnk"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "WeekCalendarTray"

if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "WeekCalendarTray.exe was not found next to this install script. Use an extracted release package."
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Assert-SafeChildPath -Path $installDir -Parent $installRoot | Out-Null
Assert-SafeChildPath -Path $stageDir -Parent $installRoot | Out-Null
Assert-SafeChildPath -Path $backupDir -Parent $installRoot | Out-Null

$previousStartupValue = $null
$startupValueExisted = $false
$runKey = Get-Item -LiteralPath $runKeyPath -ErrorAction SilentlyContinue
if ($null -ne $runKey) {
    $previousStartupValue = $runKey.GetValue(
        $runValueName,
        $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    $startupValueExisted = $null -ne $previousStartupValue
}

$startupTargetsKnownApp = (Test-StartupValueForExecutable -Value $previousStartupValue -ExecutablePath $installedExe) -or
    (Test-StartupValueForExecutable -Value $previousStartupValue -ExecutablePath $sourceExe)
if ($startupValueExisted -and -not $startupTargetsKnownApp) {
    throw "The existing WeekCalendarTray startup entry points to an unexpected command. It was not changed."
}

$startupShouldBeEnabled = if ($PreserveStartup) {
    $startupValueExisted
} else {
    -not $NoStartup
}

$sourceIsInstall = [string]::Equals(
    $sourceDir,
    (Get-NormalizedPath $installDir),
    [StringComparison]::OrdinalIgnoreCase)
$replacementPlaced = $false
$backupCreated = $false
$startupChanged = $false
$shortcutExisted = Test-Path -LiteralPath $shortcutPath -PathType Leaf
$completed = $false

try {
    if (-not $sourceIsInstall) {
        New-Item -ItemType Directory -Path $stageDir | Out-Null
        foreach ($entry in Get-ChildItem -LiteralPath $sourceDir -Force) {
            Copy-Item -LiteralPath $entry.FullName -Destination $stageDir -Recurse -Force
        }

        $stagedExe = Join-Path $stageDir "WeekCalendarTray.exe"
        if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf) -or (Get-Item -LiteralPath $stagedExe).Length -le 0) {
            throw "The staged package does not contain a valid WeekCalendarTray.exe."
        }
    }

    Stop-KnownAppProcesses -ExecutablePaths @($sourceExe, $installedExe)

    if (-not $sourceIsInstall) {
        if (Test-Path -LiteralPath $installDir) {
            Move-SafeDirectory -Source $installDir -Destination $backupDir -Parent $installRoot
            $backupCreated = $true
        }

        Move-SafeDirectory -Source $stageDir -Destination $installDir -Parent $installRoot
        $replacementPlaced = $true
    }

    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
        throw "Installed executable verification failed: '$installedExe'."
    }

    New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null
    $shell = $null
    $shortcut = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $installedExe
        $shortcut.WorkingDirectory = $installDir
        $shortcut.Description = "Week Calendar Tray"
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)
        }
        if ($null -ne $shell) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
        }
    }

    if ($startupShouldBeEnabled) {
        New-Item -Path $runKeyPath -Force | Out-Null
        Set-ItemProperty -Path $runKeyPath -Name $runValueName -Value "`"$installedExe`""
    } else {
        Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
    }
    $startupChanged = $true
    $completed = $true
}
catch {
    $originalError = $_
    try {
        if ($startupChanged) {
            if ($startupValueExisted) {
                New-Item -Path $runKeyPath -Force | Out-Null
                Set-ItemProperty -Path $runKeyPath -Name $runValueName -Value $previousStartupValue
            } else {
                Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
            }
        }

        if (-not $shortcutExisted -and (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
            Remove-Item -LiteralPath $shortcutPath -Force
        }

        if ($replacementPlaced) {
            Remove-SafeDirectory -Path $installDir -Parent $installRoot
        }
        if ($backupCreated -and (Test-Path -LiteralPath $backupDir -PathType Container)) {
            Move-SafeDirectory -Source $backupDir -Destination $installDir -Parent $installRoot
        }
    }
    catch {
        Write-Warning "Install rollback was incomplete. The previous app may remain in '$backupDir'. $($_.Exception.Message)"
    }

    throw $originalError
}
finally {
    if ($completed) {
        Remove-SafeDirectory -Path $backupDir -Parent $installRoot
    }
    Remove-SafeDirectory -Path $stageDir -Parent $installRoot
}

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installDir -WindowStyle Hidden
}

Write-Host "Week Calendar Tray installed to: $installDir"
Write-Host "Start Menu shortcut: $shortcutPath"
Write-Host "Start with Windows: $startupShouldBeEnabled"
if ($NoLaunch) {
    Write-Host "Launch was skipped."
}
