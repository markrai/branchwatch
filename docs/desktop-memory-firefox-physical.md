# Desktop Memory: Phase 8G.3B physical Firefox evidence

Status: qualification tooling only. No real Windows evidence has been collected by CI. A positive automated comparison is not a release decision and does not unlock Firefox capture or restore. Use a clean checkout of this revision, record the commit, Windows build, Firefox version, architecture, display setup and extension version (0.2.1). Record the host executable SHA256 and BranchWatch executable SHA256 used in each test run. Use disposable windows/profiles and no private browsing data.

## Setup

On Windows with PowerShell 7, .NET 10 and Node.js, run from the repository root:

```powershell
git rev-parse HEAD
dotnet test BranchWatch.sln -c Release
pwsh -File scripts/firefox-companion.ps1 -Action Unregister
pwsh -File scripts/firefox-companion.ps1 -Action Register
dotnet publish BranchWatch/BranchWatch.csproj -c Release -o "$env:LOCALAPPDATA/BranchWatch/firefox-qualification-app"
$exe = Join-Path $env:LOCALAPPDATA 'BranchWatch/firefox-qualification-app/BranchWatch.exe'
Get-FileHash $exe
Get-FileHash (Join-Path $env:LOCALAPPDATA 'BranchWatch/browser-companion/Firefox/FirefoxCompanionHost.exe')
```

Load or reload the temporary Firefox add-on from tools/firefox-companion-extension/manifest.json using about:debugging#/runtime/this-firefox. Do not replace a registration belonging to another installation; if unregistration refuses, investigate. Exit any installed BranchWatch tray instance if you want to check the hello connection with the qualification build, then start $exe. The independent probe itself can run even if the tray is closed.

## One positive window case

Open the Companion popup in a disposable normal Firefox window and choose Open correlation test tab. Enter a unique label, e.g. single-A1, and click Set label. Keep this diagnostic tab selected. Use a separate PowerShell window on the same virtual desktop to capture the independent before observation. Do not move or resize the target browser window between probes.

```powershell
$folder = Join-Path $env:LOCALAPPDATA 'BranchWatch/firefox-qualification/single-A1'
New-Item -ItemType Directory -Path $folder
function Take-FirefoxProbe([string]$stage) {
    $source = Join-Path $env:APPDATA 'BranchWatch/desktop-memory'
    $known = @(Get-ChildItem -LiteralPath $source -Filter 'desktop-memory-probe-*.json' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    $p = Start-Process -FilePath $exe -ArgumentList '--desktop-memory-probe','--include-identities' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw 'Probe failed' }
    $new = @(Get-ChildItem -LiteralPath $source -Filter 'desktop-memory-probe-*.json' | Where-Object { $_.Name -notin $known -and $_.Name -notlike '*.error.json' })
    if ($new.Count -ne 1) { throw 'Expected one new probe' }
    $destination = Join-Path $folder "$stage.probe.json"
    if (Test-Path -LiteralPath $destination) { throw 'Evidence already exists' }
    Copy-Item -LiteralPath $new[0].FullName -Destination $destination
}
Take-FirefoxProbe before
```

Return to the labeled Firefox tab, focus the window and click Run correlation challenge. Keep the window focused until the result appears. A successful result displays a JSON object containing the two-marker transcript and cleanup checks. Select and copy only that JSON text. Without closing the diagnostic tab, return to PowerShell and run:

```powershell
$challenge = Join-Path $folder 'challenge.json'
if (Test-Path -LiteralPath $challenge) { throw 'Evidence already exists' }
Get-Clipboard -Raw | Set-Content -LiteralPath $challenge -Encoding utf8NoBOM
Take-FirefoxProbe after
node scripts/firefox-qualification.cjs $challenge (Join-Path $folder 'before.probe.json') (Join-Path $folder 'after.probe.json')
```

The verifier requires all seven responses in order, two distinct nonces, equal native identity, verified cleanup, independently identified unique diagnostic title, matching HWND/PID/desktop/DPI/bounds, stable probes and chronological bracketing within 15 minutes. It reports hashes and CORRELATION_EVIDENCE_MATCH with authorized:false, or exits nonzero. It does not validate that the files were produced by the specified binaries or prove process creation independently; the operator records build and boot identity and checks that no restart occurred during the test. The report is not tamper-proof. Keep the evidence local: raw probes include titles and paths of unrelated windows. Close the diagnostic tab after the after probe. Use a fresh label and directory for every attempt.

## Required physical matrix

| Case | Required observation |
| --- | --- |
| Single window | Repeat the positive procedure twice; compare independent evidence. |
| Two different windows | Positive procedure in each, different HWNDs. |
| Identical bounds | Positive procedure in each same-position window, different HWNDs. |
| Separate profiles | Positive procedure in each independently installed profile; confirm distinct profile IDs via Check connection. |
| Separate virtual desktops | Positive procedure on each original desktop; verify Task View and different GUIDs. |
| Mixed DPI, portrait and negative coordinates | Positive procedure on each relevant display, confirm actual display configuration. |
| Focus/tab/window drift | Interrupt the challenge by switching focus, moving/resizing or changing active tab; if drift occurs during observation, result must not be positive. |
| Interrupted challenge | Navigate or close the diagnostic tab during challenge; verify marker cleanup manually, no positive result. |
| Private/maximized/minimized | Verify the diagnostic cannot qualify an unsupported target. |
| Missing host / wrong ID | Verify connection or challenge fails. |
| Chromium regression | Check Chrome/Edge capture/reopen still uses the existing guarded path. |

Record Pass, Fail or Blocked for each row, with actual observations and evidence paths for positive cases. A missing condition or unavailable hardware is Blocked, not Pass. Some interruptions are timing-sensitive; repeat to observe an interruption during the test. Confirm the test tab title is restored and the markers are not present afterward. Record exact browser and OS versions and any title-customizing extension interference. No automated matrix pass or authorization is provided by this tool. Review all results before designing live Firefox authorization.

See [the diagnostic guide](desktop-memory-firefox-qualification.md) for cleanup and [session preparation](desktop-memory-firefox-session-preparation.md) for remaining gates.
