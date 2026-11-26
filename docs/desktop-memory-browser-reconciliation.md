# Desktop Memory: Phase 8F duplicate prevention

Companion 0.4.0 adds conservative profile-wide reconciliation before the explicit Phase 8E reopen action. Reload the unpacked extension, rebuild BranchWatch, and unregister/re-register the browser native host with scripts/browser-companion.ps1. The restore operation requires protocol v2; old v1 restore clients are rejected by the current app and host. Capture and hello remain at v1.

After redeeming the one-time restore code, Companion takes two bounded inventories of normal browser windows in the already-authorized profile. It compares window and tab identities, URLs, order, pinning, active state and focus. Missing, pending, malformed, oversized or changing observations block creation. Private windows are excluded; no additional inventory leaves the browser. Only local outcome counts and the existing nonce ledger are retained.

| Outcome | Behavior |
| --- | --- |
| Already present | The focused source window exactly matches the saved ordered URL list, pinning and active state, and no other observed window overlaps. No changes. |
| Overlap | At least one saved URL occurs in a normal window. Exact matches elsewhere, partial matches, extra tabs, differing metadata and multiple candidates all block creation. No existing tabs or windows are changed. |
| Inventory unverified | Incomplete or unstable inventory, or changed source focus prevents safe creation. No new window requested. |
| Clear | No observed normal window contains any saved URL. Recheck source and permission, then use the Phase 8E new-window path. |

Duplicate occurrences within a saved window are deliberate and count by position, not by URL set. URL comparison uses browser URL canonicalization without dropping query strings or fragments. Redirected URLs or semantically equivalent but different URLs may not match. When the saved active tab was excluded, the first retained tab is the expected active tab.

This is intentionally conservative: a single shared website on a different desktop may block reopening. Normal windows in the same profile are checked even if minimized or maximized; we do not assert their native virtual desktop membership. Different profiles are not inspected. Non-normal windows are outside the inventory. Existing sessions are never merged, moved, activated or closed. The already-present result is a browser-content observation of the previously native-checked source, not proof of current native placement. An inventory is not an atomic lock on the browser; changes after the final observation remain possible. No claim of global exactly-once restoration.

All outcomes still consume the previously issued one-time request. Even if nothing opened, BranchWatch will not issue that saved-window request again during the same application run. Inspect existing windows before retrying; restart BranchWatch only after confirming it is safe. Native placement, automatic capture, closed-browser launch and physical qualification remain outstanding.

## Manual checks

- Restart Chrome/Edge with native session restoration enabled. Redeem a code from an exact restored source window and verify no duplicate.
- Try a complete matching window elsewhere in the same profile, including another virtual desktop, then partial matches, additional tabs, repeated URLs and differing pin/active state. Confirm no new window and no existing-window changes.
- Try an unrelated profile, a pending navigation, missing tab access, a focus change, and tab/window changes during inventory. Confirm fail-closed behavior.
- Verify a stable disjoint profile permits the existing new-window operation. Test query/fragment distinctions and intentional duplicate URLs.
- Inspect browser popup status after it closes; verify the one-time native approval still blocks repeated issuance. No extra URL inventory is transmitted or stored by the reconciliation path.
- Verify an old v1 restore client cannot redeem a code with the updated application. Hello and capture still use v1.

Phase 8G introduces a separate [Firefox diagnostic](desktop-memory-firefox-qualification.md). Firefox does not use Chromium's parent-window argument; no Firefox tab operation is authorized by this diagnostic.
