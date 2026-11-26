# Desktop Memory: Phase 7 hardware qualification

Status: harness implemented; physical qualification **not yet performed**. This is a release gate, not a feature expansion. Automated CI and a successful probe alone do not qualify actual Windows restoration.

## Prepare

Use PowerShell 7, .NET 10 SDK, Git, a non-elevated interactive Windows session, and a clean checkout of the exact candidate commit. Save your work and back up your local BranchWatch data before testing. Use disposable virtual desktops, harmless fixture windows and disposable project folders. The qualification build uses the normal BranchWatch application data location; the harness does not change settings, snapshots or display configuration on its own. Do not delete or corrupt real snapshots. For recovery tests, use a disposable Windows user or separately backed-up test copy.

From the repository root:

```powershell
pwsh -File scripts/desktop-memory-qualification.ps1 -Action Init -RunId dm-001
```

Init runs the solution tests, publishes the exact checkout and the standalone test-window fixture for your architecture, and creates a private evidence directory under `%LOCALAPPDATA%\BranchWatch\desktop-memory-qualification\dm-001`. It records the Git commit and SHA-256 hashes of both published executables and the test matrix. If initialization fails, choose a new run ID rather than reusing incomplete evidence.

Exit the existing tray instance manually before starting the qualification build. Do not run both builds simultaneously.

```powershell
$run = Join-Path $env:LOCALAPPDATA 'BranchWatch\desktop-memory-qualification\dm-001'
Start-Process (Join-Path $run 'app\BranchWatch.exe')
Start-Process (Join-Path $run 'fixture\DesktopMemoryFixture.exe')
```

The fixture opens an ordinary resizable WPF window with the title Desktop Memory Fixture - Default. It does not save data, launch other applications or alter the desktop. To create another distinct window, pass a short alphanumeric label:

```powershell
Start-Process (Join-Path $run 'fixture\DesktopMemoryFixture.exe') -ArgumentList 'Other'
```

Multiple Default instances produce intentionally ambiguous titles. Close all fixture instances before generic-launch qualification, and save only one Default window for that case.

## Record a case

The machine-readable [case matrix](../scripts/desktop-memory-cases.json) contains fourteen required cases. Read each case's required observations. For a passing attempt, capture before and after from the qualification build, perform the manual steps, and record factual notes describing what you observed. Captures must be made while desktop/display configuration is stationary. The script requires the exact qualification executable to be the running tray instance.

For exact reuse, save the fixture's normal layout and capture the original bounds:

```powershell
pwsh -File scripts/desktop-memory-qualification.ps1 -Action Capture -RunId dm-001 -Case exact-reuse -Stage before -IncludeIdentities
```

Move the Default fixture manually. In Settings → Desktop Memory, preview and restore the saved fixture, then repeat restoration. Verify AlreadyPlaced on the second restore and inspect unrelated windows. Capture the resulting bounds:

```powershell
pwsh -File scripts/desktop-memory-qualification.ps1 -Action Capture -RunId dm-001 -Case exact-reuse -Stage after -IncludeIdentities
```

Only if all required observations actually pass:

```powershell
pwsh -File scripts/desktop-memory-qualification.ps1 -Action Record -RunId dm-001 -Case exact-reuse -Result Pass -Notes 'Default fixture returned to original bounds; second restore AlreadyPlaced; unrelated window unchanged.'
```

Use Fail or Blocked with explanatory notes if a check fails or hardware/application is unavailable. Fail and Blocked do not require a complete capture pair and do not qualify the case. If a case needs repeating, specify `-Attempt 2` consistently for its captures and record. Prior attempts remain intact; only the latest attempt counts.

For reboot, capture the original saved Default fixture bounds with -IncludeIdentities, close other applications safely and use Windows Restart (not shutdown with fast startup). After signing in, ensure the normal installed auto-start instance is not running and manually launch the same qualification build. Do not launch the fixture yourself. Confirm there was no automatic restore; opt in to missing-app launching and restore only the Default fixture. Capture after with -IncludeIdentities. The evaluator checks a later Windows boot timestamp, fixture executable identity, matching desktop GUID and exact screen bounds. A changed desktop GUID or topology must not be silently accepted as an exact reboot restore. Record failure if the actual behavior fails. Repeat restoration and verify no duplicates manually.

For mixed-DPI/portrait and disconnect cases the evaluator checks observed display scale/orientation and reduced monitor count respectively. Other behavior (including cancellation, unrelated-window isolation, actual adapter semantics, visual quality and pinned-window behavior) is manually observed. A pass note is an operator assertion, not an automated proof of every behavior.

## Evaluate

```powershell
pwsh -File scripts/desktop-memory-qualification.ps1 -Action Evaluate -RunId dm-001
```

The evaluator lists every case and exits nonzero unless all fourteen latest attempts have recorded passes with valid before/after evidence and applicable objective checks. Missing, incomplete, blocked, failed or modified evidence cannot qualify. It checks hashes and case/attempt identity, but does not cryptographically attest human observations or prevent a local operator from editing all evidence and hashes.

The physical release gate also requires review of the notes and CI success for the exact release candidate. A different commit or architecture must be qualified separately. A passing evaluation does not qualify minimized/maximized restoration or brokered window positioning; those remain unsupported as documented.

## Privacy

Evidence stays local and is excluded by repository ignore rules. The probe also writes its original report to `%APPDATA%\BranchWatch\desktop-memory`. Even without -IncludeIdentities, monitor identifiers, process names, geometry and desktop GUIDs can identify your setup. With that switch, window titles and executable paths are included. Never upload raw reports or real snapshots to GitHub; share only redacted summaries or carefully inspected excerpts. No automatic upload is performed. Remove evidence and original probe files manually when no longer required.

CI builds the fixture and runs simulated evaluator self-tests. It does not run physical display changes, reboot, or manipulate windows.
