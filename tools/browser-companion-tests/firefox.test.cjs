const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { webcrypto } = require("node:crypto");
const context = vm.createContext({
    crypto: webcrypto,
    document: { getElementById: () => ({ addEventListener() {} }) }
});
vm.runInContext(fs.readFileSync(path.join(__dirname, "../firefox-companion-extension/popup.js"), "utf8"), context);
const { windowObservation, checkConnection, inspectWindow } = context;
const normal = { id: 7, left: -500, top: 20, width: 900, height: 700, type: "normal", state: "normal", focused: true, incognito: false };
function fixture({ windows = [normal, normal], status = "foreground-candidate-not-authorized", corrupt = false, family = "firefox" } = {}) {
    const calls = [];
    let reads = 0;
    const store = {};
    return { calls, store, api: {
        storage: { local: {
            get: async key => ({ [key]: store[key] }),
            set: async values => Object.assign(store, values)
        } },
        windows: { getCurrent: async () => windows[Math.min(reads++, windows.length - 1)] },
        runtime: { sendNativeMessage: async (host, request) => {
            calls.push({ host, request });
            if (request.command === "hello") return { version: 1, status: "connected", profileId: request.profileId, nonce: request.nonce };
            return { version: 1, windowId: request.windowId, nonce: corrupt ? "incorrect" : request.nonce, status, browser: family };
        } }
    }};
}

test("manifest grants no tab, history or host permission and excludes private mode", () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, "../firefox-companion-extension/manifest.json"), "utf8"));
    assert.deepEqual(manifest.permissions.sort(), ["nativeMessaging", "storage"]);
    assert.equal(manifest.incognito, "not_allowed");
    assert.equal(manifest.browser_specific_settings.gecko.id, "branchwatch-companion@branchwatch.local");
    assert.equal(manifest.optional_permissions, undefined);
    assert.equal(manifest.host_permissions, undefined);
    assert.equal(manifest.background, undefined);
});
test("connection reuses profile ID and sends no browsing data", async () => {
    const { api, calls, store } = fixture();
    const first = await checkConnection(api);
    const second = await checkConnection(api);
    assert.equal(first.status, "connected");
    assert.match(first.profileId, /^[0-9a-f]{32}$/);
    assert.equal(second.profileId, first.profileId);
    assert.equal(store.profileId, first.profileId);
    assert.notEqual(calls[0].request.nonce, calls[1].request.nonce);
    assert.deepEqual(Object.keys(calls[0].request).sort(), ["command", "nonce", "profileId", "version"]);
});
test("rejects private, unfocused, non-normal and invalid windows", () => {
    for (const changed of [{ incognito: true }, { focused: false }, { state: "maximized" }, { type: "popup" }, { width: 0 }, { id: -1 }])
        assert.throws(() => windowObservation({ ...normal, ...changed }));
});
test("foreground candidate is diagnostic only and sends geometry without URLs", async () => {
    const { api, calls } = fixture();
    const result = await inspectWindow(api);
    assert.equal(result.authorized, false);
    assert.equal(result.nativeObservation.status, "foreground-candidate-not-authorized");
    assert.equal(calls[0].host, "com.branchwatch.firefox_probe");
    assert.deepEqual(Object.keys(calls[0].request).sort(), ["command", "height", "left", "nonce", "top", "version", "width", "windowId"]);
});
test("rejects changed window, wrong nonce and unexpected browser family", async () => {
    for (const options of [{ windows: [normal, { ...normal, id: 8 }] }, { corrupt: true }, { family: "chrome" }, { status: "authorized" }]) {
        const { api } = fixture(options);
        await assert.rejects(() => inspectWindow(api));
    }
});
test("unverified foreground is never accepted as a mapped window", async () => {
    const { api } = fixture({ status: "parent-unverified" });
    const result = await inspectWindow(api);
    assert.equal(result.authorized, false);
    assert.equal(result.nativeObservation.status, "parent-unverified");
});
