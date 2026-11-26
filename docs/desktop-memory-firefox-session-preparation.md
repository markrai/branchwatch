# Desktop Memory: Phase 8G.3A Firefox session preparation

Phase 8G.2 is diagnostic-only. No successful physical Firefox/Windows correlation evidence has been recorded. This change prepares the data model and a pure session planner without granting live authorization. The Firefox diagnostic extension and native host keep their existing operations and permissions. No user-facing capture or restore action is added.

The snapshot model can validate and round-trip a Firefox context with a Firefox executable and MozillaWindowClass. This is a data compatibility change, not a way to capture URLs. Existing Chrome/Edge contexts remain valid. The live browser classifier, capture inbox, capture/restore envelope parsers and restore queue continue to reject Firefox. Saved Firefox contexts are labeled correctly and cannot generate a browser reopening code; the ordinary restore engine continues to exclude windows with browser context.

The offline planner accepts two synthetic ordered observations for the same window and the original active tab ID. It requires complete non-private tab identities, stable URL and metadata observations, and explicit cookie store identity. It excludes non-default cookie stores rather than converting container tabs to the default browsing context, and separately counts unsupported URLs. A missing cookie store identity or changed observation fails closed. Only HTTP(S) URLs, pinning, order and original active selection are retained. No cookie values are read. Container restoration is not implemented. The planner is tested with synthetic data and is not loaded by the Firefox extension. Shared Chromium restoration validation/reconciliation is tested against the resulting default-store plan, not invoked on live Firefox.

## Remaining gate

Run the two-marker qualification from the Firefox guide on real Windows with multiple same-position windows, profiles, desktops and DPI configurations. Record the independently verified native HWND and marker cleanup. Before any live Firefox URL operation, design and test a one-time native authorization flow binding the proof to the exact draft or saved session, the correct profile, desktop, and the tab inventory. An old diagnostic reply is insufficient; it must not be treated as a reusable authorization token. The active diagnostic tab must be excluded from saved tabs and the prior active tab handled explicitly. Do not request cookies permission solely to recreate container tabs. Profile identity and container membership require physical qualification.

Next: physical correlation qualification, then live Firefox capture and explicit save, then profile-aware reopening with duplicate prevention and verified default-store handling. Automatic capture and native placement remain outstanding. No version or wire-protocol bump in this preparatory slice.

References: [Firefox tab metadata](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/tabs/Tab), [contextual identities](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Work_with_contextual_identities).

Phase 8G.3B provides [physical qualification tooling](desktop-memory-firefox-physical.md); live operations remain locked until the actual matrix is reviewed.
