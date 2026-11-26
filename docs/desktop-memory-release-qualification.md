# Desktop Memory: Phase 8H.3 release qualification

Status: implementation and qualification tooling; interactive Windows evidence is still required. This phase does not authorize automatic browser capture, absent-browser launch, native browser window placement or live Firefox capture/restoration.

## Candidate scope

The candidate supports manually saving layouts, previewing and selectively restoring eligible normal windows, explicit guarded application/folder launches, and experimental Chrome/Edge Companion capture and reopening. Browser selection, one-time approvals, conservative duplicate prevention, and current-user DPAPI snapshot storage are implemented. Firefox remains diagnostic-only. Companion installation is a development workflow, not a signed production extension distribution.

## Run in order

1. Confirm the exact candidate commit has passing Windows tests and x64/arm64 packaging. Keep CI results and artifact identifiers. Use a clean checkout and disposable Windows account, profiles, desktops, folders and test data. Never experiment on actual snapshots.
2. Initialize a fresh hardware qualification run using the [existing harness](desktop-memory-hardware.md). It records the commit and executable hashes. The 21-case [matrix](../scripts/desktop-memory-cases.json) replaces the older 14-case matrix; evaluate only from a clean checkout of the same commit and matching matrix. Do not reuse old run results for a new revision.
3. Complete the original layout, placement, reboot, topology, selection, adapters, persistence and existing-feature cases.
4. Complete the [Chromium parent correlation](desktop-memory-browser-qualification.md), [capture](desktop-memory-browser-capture.md), [reopen](desktop-memory-browser-restore.md) and [reconciliation](desktop-memory-browser-reconciliation.md) procedures for Chrome and Edge with Companion 0.4.1. Record the actual browser versions, extension versions, host binary hashes, display/desktop configurations and test observations locally. No old client should be assumed to have the new safeguards.
5. Complete protected storage and account isolation checks below. Verify the [Firefox diagnostic-only boundary](desktop-memory-firefox-physical.md) and Companion install/upgrade/unregister checks.
6. Record each case using actual observations and before/after probes. A case whose conditions cannot be tested is Blocked. The harness checks stable observations and selected objective evidence, but the additional browser/storage cases also depend on manual assertions. A passing evaluator output is not proof of every assertion or authorization to enable blocked features.

## Recovery and account checks

Use a disposable original Windows account and disposable second account with loaded profiles; do not change credentials or transfer DPAPI keys. Back up synthetic data locally before intentional failure tests. Record the relevant file hashes before each action. Keep raw evidence private.

- Create or copy synthetic schema 1 primary and previous-generation backup. Verify read-only load preserves hashes; explicit protection changes both to schema 2 without changing saved contents or timestamps. A UTF-8 BOM legacy file must remain readable.
- Simulate a mixed pair by retaining the synthetic plaintext primary and restoring a copy of its already protected backup. With BranchWatch stopped during file changes, restart and complete migration; verify the encrypted backup remains unchanged.
- Save and delete a synthetic layout and verify both on-disk files contain only SchemaVersion, Protection and Payload. Check same-user reads after restart. Inspect known synthetic values in the UI, not by exporting decrypted real data.
- Close BranchWatch; move the primary aside and copy the protected previous-generation backup to the primary path. Restart and verify only the expected previous generation returns; preserve the backup. A missing primary does not automatically recover from backup.
- In a second disposable Windows account, copy only the synthetic encrypted files to that account's normal test storage location. Confirm the application cannot load or replace them and both hashes remain unchanged. Return to the original account and verify the original protected pair is readable. This tests account separation, not resistance to code already running as the original user.
- Tamper with a synthetic encrypted payload or corrupt a synthetic backup. Confirm the operation fails closed without overwriting either generation. Restore a known-good test copy with the app stopped.

The additional regression tests exercise migration recovery and tampering under the CI runner's Windows account. They do not substitute for cross-account, restart or physical browser testing.

## Evaluate

```powershell
pwsh -File scripts/desktop-memory-qualification.ps1 -Action Evaluate -RunId dm-001
```

A missing, failed, blocked or invalid case yields a nonzero exit code. A passing output records manual assertions plus evidence checks for the exact candidate. Independently review the notes and browser evidence before deciding release readiness. Keep the current experimental limitations prominent in release notes. The separate Firefox physical matrix must be completed and reviewed before designing live authorization; it is not replaced by the diagnostic-only boundary case. Signed extension distribution, unattended capture, native browser placement and closed-browser launch remain future work. No release or live Firefox unlock is performed by this script.
