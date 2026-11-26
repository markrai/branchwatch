# Desktop Memory: Phase 8H.1 browser security review

Scope: Chromium capture, reopen and reconciliation source review; Firefox diagnostic and authorization gate status. This is not a full security audit or a physical qualification result.

## Addressed findings

| Finding | Change |
| --- | --- |
| Capture filtered unexpected window/private tab identities before comparing observations | Reject incomplete or inconsistent inventory, including duplicate IDs, index gaps, missing URL/metadata and missing or multiple active tabs. Unsupported and pending URLs are still omitted from an otherwise valid inventory. |
| A new-window response could reuse an ID in the previously observed inventory | Reject observed pre-existing normal window IDs and tab IDs before changing any tab. This does not guarantee identity across unobserved windows or browser races. |
| Restore verified URLs initially, but checked only membership before individual updates and omitted URL/privacy checks in final verification | Recheck ID, order, window, privacy, URL and metadata before update and final reporting. A changed destination yields partial, not opened. A matching pending URL is an intended navigation, not proof of completed page load. |
| Capture and restore popup actions could overlap | Disable the other action while an explicit capture or restore is pending. This is a popup-level guard, not a global browser lock. |
| JavaScript URL validation allowed some raw control characters that .NET rejects | Share Chromium capture/restore URL validation and reject raw C0/C1 controls. |

Companion 0.4.1 includes these changes. Reload it in all profiles used for testing. No native host or wire protocol change is required; older extension versions remain compatible and do not receive the hardening automatically. These are development clients, not a security boundary against malicious extension code. Regression tests use mocked browser APIs, not a real browser.

## Outstanding boundaries and release gates

- Physical Firefox two-marker qualification: Phase 8G.3B tooling is available, but no passing physical results are recorded in the repository. Firefox live capture and restore remain disabled. Do not remove that gate for convenience.
- Chromium parent HWND association is still experimental. Qualify identical bounds, multiple profiles, desktop changes and mixed DPI on actual Windows. No automatic background URL inventory or native window placement is authorized by the existing handshake.
- Same-user native messaging and named pipe: an allowlisted extension and local-user pipe restrict the intended transport, not all programs running as that user. Random profile identifiers and one-time codes do not establish a cryptographic identity for browser profiles. Copied profile data can duplicate identity.
- Snapshot privacy: Phase 8H.2 protects the complete snapshot and backup with current-user DPAPI after migration. Legacy plaintext files remain until migration; historical copies and diagnostic reports are outside this protection. No secure erasure or same-user isolation is claimed. See [snapshot protection](desktop-memory-snapshot-protection.md).
- Browser operations and virtual desktop checks are not atomic. An inventory can become stale after the last observation; browser redirects, focus changes and concurrent user actions may produce a partial or uncertain result. Do not retry blindly or close existing windows automatically.
- Duplicate prevention is profile-wide and intentionally conservative; an overlap on another desktop may prevent reopening. No cross-profile inventory, closed-browser launch, automatic reconciliation/merging, or native placement qualification exists.
- Temporary extension installation is for development. Signed distribution, upgrade compatibility, uninstall cleanup and live browser/Windows regression testing remain to be qualified.

## Validation plan

Run the full Windows CI and the existing Chromium and Firefox physical matrices. Include changed navigation during reopening, a reused or ambiguous native identity, denied permission, two overlapping popup operations, expired approvals, wrong profile and backup privacy checks. Use disposable data and record actual observations. Never infer a physical pass from CI. Phase 8H.1 is a source-level hardening slice; final qualification remains open.

Phase 8H.3 consolidates the outstanding physical and recovery checks into the [release qualification procedure](desktop-memory-release-qualification.md). No passing physical run is recorded by CI, and no unsupported feature is enabled.
