const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const context = vm.createContext({ URL, TextEncoder });
const extension = path.join(__dirname, "../firefox-companion-extension");
vm.runInContext(fs.readFileSync(path.join(extension, "session-plan.js"), "utf8"), context);
vm.runInContext(fs.readFileSync(path.join(__dirname, "../browser-companion-extension/restore.js"), "utf8"), context);
const { planFirefoxCapture, validRestoreTabs, reconcileProfile } = context;
const profile = "0123456789abcdef0123456789abcdef";
const url = "https://example.com/work?x=1#section";
function tab(index, options = {}) {
    return { id: 100 + index, windowId: 7, incognito: false, index, cookieStoreId: "firefox-default",
        url, pinned: index === 0, active: index === 1, ...options };
}
const normal = [tab(0), tab(1), tab(2, { url: "about:newtab" })];
function plan(first, second = first, active = 101) {
    return planFirefoxCapture(first, second, 7, profile, active);
}

test("pure Firefox plan preserves order, duplicate occurrences and original active tab", () => {
    const result = plan(normal);
    assert.equal(result.context.browser, "firefox");
    assert.equal(result.context.profileId, profile);
    assert.equal(JSON.stringify(result.context.tabs), JSON.stringify([
        { url, pinned: true, active: false }, { url, pinned: false, active: true }
    ]));
    assert.equal(result.omittedUnsupported, 1);
    assert.equal(result.omittedContainer, 0);
    assert.equal(validRestoreTabs(result.context.tabs), true);
    assert.equal(plan(normal, normal, 102).context.tabs.some(t => t.active), false);
});

test("container tabs are excluded rather than silently converted to the default store", () => {
    const live = [tab(0), tab(1, { cookieStoreId: "firefox-container-3" }), tab(2)];
    const result = plan(live);
    assert.equal(result.omittedContainer, 1);
    assert.equal(result.context.tabs.length, 2);
    assert.equal(result.context.tabs.some(t => t.active), false);
    const missing = live.map(t => ({ ...t }));
    delete missing[1].cookieStoreId;
    assert.equal(plan(missing), null);
});

test("unsafe, private, missing, unstable and oversized observations fail closed or omit unsupported URLs", () => {
    for (const change of [
        { windowId: 8 }, { incognito: true }, { id: 100 }, { index: 3 },
        { pendingUrl: "https://example.org/" }, { url: undefined }, { cookieStoreId: undefined }
    ]) assert.equal(plan([normal[0], { ...normal[1], ...change }, normal[2]]), null);
    assert.equal(plan(normal, [normal[0], { ...normal[1], url: "https://example.org/" }, normal[2]]), null);
    assert.equal(plan(normal, normal, 999), null);
    assert.equal(plan([tab(0, { url: "file:///secret", active: true }), tab(1, { url: "https://user:pass@example.com", active: false })], undefined, 100), null);
    assert.equal(plan(Array.from({length: 101}, (_, i) => tab(i))), null);
});

test("shared restore reconciliation recognizes exact default-store planned session without opening", () => {
    const desired = plan(normal).context.tabs;
    const inventory = [{ id: 7, state: "normal", focused: true, tabs: desired.map((t, i) => ({ ...t, id: i + 100 })) }];
    assert.equal(reconcileProfile(inventory, 7, desired).status, "already-present");
    inventory.push({ id: 8, state: "normal", focused: false, tabs: [{ ...desired[0], id: 500 }] });
    assert.equal(reconcileProfile(inventory, 7, desired).status, "overlap");
});

test("the offline planner is not loaded or given live tab permissions by the Firefox extension", () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(extension, "manifest.json"), "utf8"));
    assert.deepEqual(manifest.permissions.sort(), ["nativeMessaging", "storage"]);
    assert.equal(manifest.optional_permissions, undefined);
    for (const file of ["popup.html", "challenge.html", "popup.js", "challenge.js"])
        assert.equal(fs.readFileSync(path.join(extension, file), "utf8").includes("session-plan.js"), false);
});
