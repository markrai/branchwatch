# BranchWatch R.8 architectural review and regression qualification

Review baseline: `6f92fc1f23e6c524e2f1c27d6a666aa1047ed820` (merged R.7). This review is a focused audit of the refactor boundaries, lifecycle, confirmation and authorization paths, and Windows validation workflow; it is not a new security audit of every source file.

## Automated baseline

[R.7 Windows validation, run 63](https://github.com/markrai/branchwatch/actions/runs/45874082648), tested PR head `e11150741456698fab056857355c1f7679e90e0e` before merge:

- 244 solution tests passed, with warnings treated as errors; zero failures or skips.
- 18 Chromium Companion tests and 21 Firefox Companion tests passed.
- Desktop Memory fixture and Chromium host built; browser correlation and Firefox host protocol self-tests passed.
- Installer and JavaScript syntax validation and simulated Desktop Memory evidence checks passed.
- Both win-x64 and win-arm64 single-file packages published successfully.

These results are for R.7, not the R.8 candidate. Require a fresh passing Windows workflow and artifacts for the exact R.8 candidate before accepting its automated regression gate. The Windows runner is not a substitute for interactive physical testing or an ARM64 runtime test.

## Reviewed ownership and findings

| Boundary | Assessment |
| --- | --- |
| Sticky-note persistence and interaction (R.0–R.1) | Controller owns persistence, alarms and window assignment. Interaction owns editing and rename transitions. Ordinary close and application disposal are distinct. Save failures remain best-effort at shutdown; cancelled session ending is unqualified. |
| Passive body lifecycle (R.2) | Dedicated owner handles candidates, invalidation and fallback. Native surface retains affinity/style/DPI checks. Actual desktop switches, native ownership and click-through are physical gates. |
| Capture and drafts (R.3) | Capture service provides observations and transient tokens; draft owns unsaved changes. Native inspection and browser correlation are not atomic. |
| Restore session (R.4) | Preparation, approval, cancellation and progress have one owner. Page supplies explicit confirmation and awaits worker completion before disposal. Cancellation cannot undo already-issued operations. |
| Browser authorization (R.5) | Session owns ticket and approval revisions; queue rechecks exact pending approval after external verification. Same-user IPC is not an independent authentication boundary. Firefox remains diagnostic-only and native browser placement remains blocked. |
| Tray and settings (R.6) | Menu owns resources and projection is separated from command dispatch. Settings host owns window reuse and disposal. Real notification-area focus, startup registry and modal sequencing need interactive validation. |
| UI refresh and test runner (R.7) | Repository notifications coalesce, explicit refreshes remain synchronous; disposal invalidates queued work. Immediate refresh, rendering and disposal are UI-thread operations. STA runner retains original failure stacks, not a general asynchronous UI harness. |
| Confirmation lifecycle (R.8) | Found inconsistent post-modal guards for capture, deletion and legacy protection. Centralize confirmation invalidation on hide, stop, selection change and reload, and recheck selected desktop, active desktop projection, exact draft/saved references, busy and storage state. Reject reentrant prompts. Save/delete retain store-level expected-capture guards; restore retains session and native checks. Four focused tests cover the policy. |

Confirmation validation uses UI-observed state; it is not a substitute for fresh native desktop or saved-store verification. No new permission, browser support, schema, dependency or version change. The existing physical release gates remain in effect.

## Interactive acceptance checklist

Use a disposable Windows user, test repositories, desktops, notes and browser profiles. Back up real data, exit the installed tray instance, and use a clean checkout with the candidate artifact hash recorded. Never use private URLs or corrupt real snapshots. Record OS build, architecture, display topology, DPI, exact candidate commit, executable SHA-256 and actual observations. Mark each item Pass, Fail or Blocked; unperformed items remain unqualified.

| ID | Manual procedure and expected result | Initial status |
| --- | --- | --- |
| R8-01 | Create note, type immediately, Escape, re-enter editing, rename and cancel rename. Collapse/expand and change appearance. No duplicate body, lost text or unintended opacity changes. | Not run |
| R8-02 | Schedule a one-shot AM/PM alarm, test midnight/noon and dismiss by header. Restart before and after due time; verify pending/ringing/dismissed state and no repeat after acknowledgement. | Not run |
| R8-03 | Exit with an unfinished title/body and an open note; reopen and verify text, bounds and open state. Close another note explicitly and verify it remains closed. Test session ending/cancellation separately. | Not run |
| R8-04 | Switch virtual desktops and move notes across monitors with different DPI. Check click-through, alignment, owner affinity and fallback when native verification fails. | Not run |
| R8-05 | Exercise tray Git/workspace commands and rapid repository updates, switch settings pages, minimize/reopen, preserve sticky-note drafts, and reopen a note by ID. Exit while refresh work is pending; no duplicate settings windows or post-disposal UI operations. | Not run |
| R8-06 | With disposable snapshots, attempt save/delete/protect/recapture prompts while the original page or selection is invalidated where feasible; no stale operation should proceed. Verify declined prompts have no side effects and a fresh valid prompt still works. | Not run |
| R8-07 | Run the full [Desktop Memory release qualification](desktop-memory-release-qualification.md) and [hardware harness](desktop-memory-hardware.md) on the candidate, including restore cancellation, protected recovery, browser duplicate prevention and companion lifecycle. | Not run |
| R8-08 | Run the [Firefox diagnostic physical procedure](desktop-memory-firefox-physical.md); verify live capture/restore remains disabled regardless of diagnostic result. | Not run |
| R8-09 | Exercise startup registry toggle and clean exit/restart on supported Windows architectures. Verify packaging and actual launch separately for x64 and ARM64 where hardware exists. | Not run |

## Exit criteria

- R.8 automated regression and packaging checks pass for the exact candidate.
- Source-review findings are addressed or explicitly bounded in the roadmap. No unresolved code-level blocker was found in the reviewed slices beyond the confirmation gap addressed here; this is not a claim of defect-free software.
- Physical release qualification is tracked separately: do not mark cases passed without observations, and do not enable blocked native/browser features on the strength of CI alone.

Feature development may resume after the code-level review and automated gate are accepted, but release or claims of physical qualification remain subject to the existing acceptance requirements.
