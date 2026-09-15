[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9]+(?:-[a-z0-9]+)+$')]
    [string]$Runtime = "win-x64",
    [switch]$NoZip,
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SigningCertificateThumbprint,
    [ValidatePattern('^https?://')]
    [string]$TimestampServer = "http://timestamp.acs.microsoft.com"
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

function Remove-SafeFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $safePath = Assert-SafeChildPath -Path $Path -Parent $Parent
    if (Test-Path -LiteralPath $safePath) {
        $item = Get-Item -LiteralPath $safePath -Force
        if ($item.PSIsContainer) {
            throw "Refusing file delete because '$safePath' is a directory."
        }

        Remove-Item -LiteralPath $safePath -Force
    }
}

function Move-SafeItem {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$Parent
    )

    $safeSource = Assert-SafeChildPath -Path $Source -Parent $Parent
    $safeDestination = Assert-SafeChildPath -Path $Destination -Parent $Parent
    if (-not (Test-Path -LiteralPath $safeSource)) {
        throw "Move source does not exist: '$safeSource'."
    }

    if (Test-Path -LiteralPath $safeDestination) {
        throw "Move destination already exists: '$safeDestination'."
    }

    Move-Item -LiteralPath $safeSource -Destination $safeDestination
}

function Get-CodeSigningCertificate {
    param([Parameter(Mandatory)][string]$Thumbprint)

    $normalizedThumbprint = $Thumbprint.Replace(" ", "").ToUpperInvariant()
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
    if ($null -eq $certificate) {
        $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
    }

    if ($null -eq $certificate) {
        throw "Code-signing certificate '$normalizedThumbprint' was not found in Cert:\CurrentUser\My or Cert:\LocalMachine\My."
    }
    if (-not $certificate.HasPrivateKey) {
        throw "Code-signing certificate '$normalizedThumbprint' does not have an accessible private key."
    }
    if ($null -eq $certificate.GetRSAPublicKey()) {
        throw "Code-signing certificate '$normalizedThumbprint' is not RSA. Smart App Control requires RSA signatures."
    }

    return $certificate
}

function Get-PackageSignableFiles {
    param([Parameter(Mandatory)][string]$PackageRoot)

    return @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse |
        Where-Object { $_.Extension -in @(".exe", ".dll", ".ps1") })
}

function Assert-PackageSignatures {
    param([Parameter(Mandatory)][string]$PackageRoot)

    $signableFiles = Get-PackageSignableFiles -PackageRoot $PackageRoot
    if ($signableFiles.Count -eq 0) {
        throw "No executable, library, or PowerShell files were found to sign in '$PackageRoot'."
    }

    foreach ($signableFile in $signableFiles) {
        $signature = Get-AuthenticodeSignature -LiteralPath $signableFile.FullName
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Invalid Authenticode signature for '$($signableFile.FullName)': $($signature.Status) $($signature.StatusMessage)"
        }
        if ($null -eq $signature.SignerCertificate -or $null -eq $signature.SignerCertificate.GetRSAPublicKey()) {
            throw "'$($signableFile.FullName)' is not signed with an RSA certificate."
        }
        if ($null -eq $signature.TimeStamperCertificate) {
            throw "'$($signableFile.FullName)' is missing an RFC 3161 timestamp."
        }
    }
}

function Sign-PackageFiles {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory)][string]$TimestampUrl
    )

    foreach ($signableFile in (Get-PackageSignableFiles -PackageRoot $PackageRoot)) {
        $result = Set-AuthenticodeSignature `
            -LiteralPath $signableFile.FullName `
            -Certificate $Certificate `
            -TimestampServer $TimestampUrl `
            -HashAlgorithm SHA256
        if ($result.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Signing '$($signableFile.FullName)' failed: $($result.Status) $($result.StatusMessage)"
        }
    }
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).ProviderPath
$projectPath = Join-Path $repoRoot "src\WeekCalendarTray\WeekCalendarTray.csproj"
$distRoot = Join-Path $repoRoot "dist"
$publishDir = Join-Path $distRoot "WeekCalendarTray"
$zipPath = Join-Path $distRoot "WeekCalendarTray-$Runtime.zip"
$operationId = [Guid]::NewGuid().ToString("N")
$stageRoot = Join-Path $distRoot ".publish-stage-$operationId"
$stagedApp = Join-Path $stageRoot "WeekCalendarTray"
$stagedZip = Join-Path $stageRoot "WeekCalendarTray-$Runtime.zip"
$publishBackup = Join-Path $distRoot ".publish-backup-$operationId"
$zipBackup = Join-Path $distRoot ".zip-backup-$operationId.zip"
$signingCertificate = $null
if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    $signingCertificate = Get-CodeSigningCertificate -Thumbprint $SigningCertificateThumbprint
}

$requiredSources = @(
    $projectPath,
    (Join-Path $PSScriptRoot "Install-WeekCalendarTray.ps1"),
    (Join-Path $PSScriptRoot "Uninstall-WeekCalendarTray.ps1"),
    (Join-Path $repoRoot "README.md"),
    (Join-Path $repoRoot "CHANGELOG.md"),
    (Join-Path $repoRoot "CONTRIBUTING.md"),
    (Join-Path $repoRoot "docs\RELEASING.md"),
    (Join-Path $repoRoot "docs\TROUBLESHOOTING.md")
)

foreach ($requiredSource in $requiredSources) {
    if (-not (Test-Path -LiteralPath $requiredSource -PathType Leaf)) {
        throw "Required release input is missing: '$requiredSource'."
    }
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
Assert-SafeChildPath -Path $stageRoot -Parent $distRoot | Out-Null
New-Item -ItemType Directory -Path $stagedApp -Force | Out-Null

$publishArguments = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", "true",
    "--nologo",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $stagedApp
)

$publishPlaced = $false
$publishBackedUp = $false
$zipPlaced = $false
$zipBackedUp = $false
$completed = $false

try {
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE. Existing release output was not replaced."
    }

    Get-ChildItem -LiteralPath $stagedApp -Filter "*.pdb" -File -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Install-WeekCalendarTray.ps1") -Destination $stagedApp
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Uninstall-WeekCalendarTray.ps1") -Destination $stagedApp
    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $stagedApp
    Copy-Item -LiteralPath (Join-Path $repoRoot "CHANGELOG.md") -Destination $stagedApp
    Copy-Item -LiteralPath (Join-Path $repoRoot "CONTRIBUTING.md") -Destination $stagedApp

    $stagedDocs = Join-Path $stagedApp "docs"
    New-Item -ItemType Directory -Path $stagedDocs | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\RELEASING.md") -Destination $stagedDocs
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs\TROUBLESHOOTING.md") -Destination $stagedDocs

    $stagedExe = Join-Path $stagedApp "WeekCalendarTray.exe"
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
        throw "Published package is missing WeekCalendarTray.exe. Existing release output was not replaced."
    }

    if ((Get-Item -LiteralPath $stagedExe).Length -le 0) {
        throw "Published WeekCalendarTray.exe is empty. Existing release output was not replaced."
    }

    foreach ($packageFile in @(
        "Install-WeekCalendarTray.ps1",
        "Uninstall-WeekCalendarTray.ps1",
        "README.md",
        "CHANGELOG.md",
        "CONTRIBUTING.md",
        "docs\RELEASING.md",
        "docs\TROUBLESHOOTING.md"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $stagedApp $packageFile) -PathType Leaf)) {
            throw "Published package is missing '$packageFile'. Existing release output was not replaced."
        }
    }

    if ($null -ne $signingCertificate) {
        Sign-PackageFiles -PackageRoot $stagedApp -Certificate $signingCertificate -TimestampUrl $TimestampServer
        Assert-PackageSignatures -PackageRoot $stagedApp
    }

    if (-not $NoZip) {
        Compress-Archive -Path $stagedApp -DestinationPath $stagedZip
        if (-not (Test-Path -LiteralPath $stagedZip -PathType Leaf) -or (Get-Item -LiteralPath $stagedZip).Length -le 0) {
            throw "Staged release zip was not created correctly. Existing release output was not replaced."
        }
    }

    if (Test-Path -LiteralPath $publishDir) {
        Move-SafeItem -Source $publishDir -Destination $publishBackup -Parent $distRoot
        $publishBackedUp = $true
    }

    Move-SafeItem -Source $stagedApp -Destination $publishDir -Parent $distRoot
    $publishPlaced = $true

    if (-not $NoZip) {
        if (Test-Path -LiteralPath $zipPath) {
            Move-SafeItem -Source $zipPath -Destination $zipBackup -Parent $distRoot
            $zipBackedUp = $true
        }

        Move-SafeItem -Source $stagedZip -Destination $zipPath -Parent $distRoot
        $zipPlaced = $true
    }

    $completed = $true
}
catch {
    $originalError = $_
    try {
        if ($zipPlaced) {
            Remove-SafeFile -Path $zipPath -Parent $distRoot
        }
        if ($zipBackedUp -and (Test-Path -LiteralPath $zipBackup)) {
            Move-SafeItem -Source $zipBackup -Destination $zipPath -Parent $distRoot
        }
        if ($publishPlaced) {
            Remove-SafeDirectory -Path $publishDir -Parent $distRoot
        }
        if ($publishBackedUp -and (Test-Path -LiteralPath $publishBackup)) {
            Move-SafeItem -Source $publishBackup -Destination $publishDir -Parent $distRoot
        }
    }
    catch {
        Write-Warning "Release rollback was incomplete. Preserved backup paths may remain in '$distRoot'. $($_.Exception.Message)"
    }

    throw $originalError
}
finally {
    if ($completed) {
        Remove-SafeDirectory -Path $publishBackup -Parent $distRoot
        Remove-SafeFile -Path $zipBackup -Parent $distRoot
    }

    Remove-SafeDirectory -Path $stageRoot -Parent $distRoot
}

Write-Host "Release created at: $publishDir"
if (-not $NoZip) {
    Write-Host "Zip created at: $zipPath"
}
