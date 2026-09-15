# Releasing Week Calendar Tray

This project publishes a self-contained `win-x64` zip. New GitHub releases are Authenticode-signed with RSA and RFC 3161 timestamped using Azure Artifact Signing. The repository workflow creates a GitHub release from a semantic version tag, but maintainers remain responsible for reviewing and creating the tag.

## 0. Configure Production Signing Once

Smart App Control may block unknown or unsigned executables, DLLs, installers, and scripts. A GitHub account alone cannot establish publisher trust: set up a publicly trusted code-signing identity first. The workflow uses Azure Artifact Signing so no code-signing private key is stored in GitHub.

1. Create and identity-validate an Azure Artifact Signing account and an RSA public-trust certificate profile. The account endpoint must match the account's Azure region.
2. Create a Microsoft Entra application/service principal, grant it the `Artifact Signing Certificate Profile Signer` role for that certificate profile, and add a federated credential restricted to this repository's `release-signing` GitHub environment.
3. In GitHub, create an environment named `release-signing`. Require an approval reviewer if appropriate. Add these environment secrets:
   * `AZURE_CLIENT_ID`
   * `AZURE_TENANT_ID`
   * `AZURE_SUBSCRIPTION_ID`
   * `AZURE_ARTIFACT_SIGNING_ENDPOINT` (for example, `https://<region>.codesigning.azure.net/`)
   * `AZURE_ARTIFACT_SIGNING_ACCOUNT_NAME`
   * `AZURE_ARTIFACT_SIGNING_CERTIFICATE_PROFILE_NAME`
4. Tag releases only after this configuration succeeds. The workflow fails closed if it cannot sign or validate every shipped `.exe`, `.dll`, and `.ps1` file. The release archive is created only after validation.

For a manual release using a certificate from a CA in the Microsoft Trusted Root Program, use the local publishing option below. Do not commit a `.pfx`, certificate password, or Azure credential.

## 1. Prepare The Version

1. Decide the release version. Version `1.2.0` is the current release-candidate target; `1.1.1` remains the installed baseline until final verification.
2. Set `Version` and `FileVersion` in `src\WeekCalendarTray\WeekCalendarTray.csproj`.
3. Move the relevant `CHANGELOG.md` section from release-candidate wording to a dated release only after final verification.
4. Confirm the README and project context match the integrated behavior.

The tag, project metadata, and changelog must agree. The publish script does not override version metadata.

## 2. Verify Locally

Use a local checkout on Windows, not a network share:

```powershell
dotnet restore .\WeekCalendarTray.sln
dotnet build .\WeekCalendarTray.sln -c Release --no-restore
dotnet run --project .\tests\WeekCalendarTray.SmokeTests\WeekCalendarTray.SmokeTests.csproj -c Release --no-build
dotnet run --project .\tests\WeekCalendarTray.UiTests\WeekCalendarTray.UiTests.csproj -c Release --no-build -- --artifacts=artifacts/ui
.\scripts\Publish-Release.ps1 -Runtime win-x64
```

Record the actual results. Do not carry forward success claims from an older build.

To produce a manually signed package, install an RSA code-signing certificate with its private key in the current-user or local-machine `My` certificate store, then run:

    .\scripts\Publish-Release.ps1 -Runtime win-x64 -SigningCertificateThumbprint <40-hex-character-thumbprint>

The script signs and validates every shipped `.exe`, `.dll`, and `.ps1` file before creating the zip. It uses the Microsoft Artifact Signing timestamp service by default; pass `-TimestampServer` only when your certificate provider requires a different RFC 3161 endpoint.

Inspect `dist\WeekCalendarTray-win-x64.zip` and confirm it contains one top-level `WeekCalendarTray` folder with:

- `WeekCalendarTray.exe`
- `Install-WeekCalendarTray.ps1`
- `Uninstall-WeekCalendarTray.ps1`
- `README.md`
- `CHANGELOG.md`
- `CONTRIBUTING.md`
- `docs\RELEASING.md`
- `docs\TROUBLESHOOTING.md`

## 3. Install And Exercise

Extract the zip to a local temporary folder and install it:

```powershell
.\Install-WeekCalendarTray.ps1 -PreserveStartup
```

Confirm the app runs from `%LOCALAPPDATA%\WeekCalendarTray\App\WeekCalendarTray.exe`, only one tray instance remains, the existing startup preference is preserved, and existing user data is unchanged. Exercise popup navigation, the clean Settings layout, default `System` plus explicit `Light`/`Dark` modes, live appearance preview, `Save` persistence, unsaved `Close` reversion, local event add/delete, an iCal sync failure and success, prayer panel behavior, first-show/reopen glass, surface opacity, Windows accent in every theme mode, tooltip dismissal, singleton details, long detail content, fallback behavior, and unchanged source-colored day segments.

Record untested criteria explicitly. In particular, do not mark display sleep/resume or Remote Desktop behavior as passed unless those transitions were actually exercised.

Review `%LOCALAPPDATA%\WeekCalendarTray\Logs` for sanitized diagnostic entries and Windows Event Viewer for native failures. Never attach private calendar URLs or event payloads to a public release issue. Historical native crash events do not prove the new build is fixed.

## 4. Create The Release Tag

The workflow accepts tags shaped like `vMAJOR.MINOR.PATCH`, for example `v1.2.0`. Tagged CI checks that the tag matches the project version, then runs the same Windows build, smoke-test harness, and staged publish steps. A protected signing job then downloads that package, signs and validates its executables, libraries, and PowerShell installer scripts, recreates the zip, and uploads the signed artifact. The release job uses that exact signed artifact to create the GitHub release with generated notes. Branch and pull-request builds upload an unsigned package artifact for test purposes only; they never create a release.

The release job uses the repository-provided `GITHUB_TOKEN` with `contents: write`. No personal access token or repository secret is required. Branch and pull-request workflows upload the zip as a run artifact but do not create releases.

Tag creation and pushing are deliberate maintainer actions and are not performed by the release script.

## 5. After Release

- Download the release asset from GitHub and verify its name and contents.
- Check that the release title and generated notes do not expose private information.
- Install the downloaded asset on a clean or representative Windows user profile.
- Open a new `Unreleased` changelog section for subsequent work.

Never add or commit private signing material. If publisher identity or signing configuration changes, expect SmartScreen reputation to rebuild; keep the same verified publisher identity whenever possible.
