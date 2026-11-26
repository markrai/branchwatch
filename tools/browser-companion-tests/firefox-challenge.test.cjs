const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { webcrypto } = require("node:crypto");
const context = vm.createContext({ crypto: webcrypto, document: { getElementById: () => ({ addEventListener() {} }) } });
vm.runInContext(fs.readFileSync(path.join(__dirname, "../firefox-companion-extension/challenge.js"), "utf8"), context);
const { runChallenge } = context;
const title = "BranchWatch Firefox correlation test";

function fixture({ alter, changeFocusAt, missingTab = false, privateTab = false } = {}) {
    const page = { title };
    const calls = [];
    const tab = { id: 42, windowId: 7, incognito: privateTab };
    const win = { id: 7, left: -500, top: 20, width: 900, height: 700, type: "normal", state: "normal", focused: true, incognito: false };
    const api = {
        tabs: { getCurrent: async () => missingTab ? undefined : tab, query: async () => [tab] },
        windows: { getCurrent: async () => ({ ...win, focused: changeFocusAt === undefined || calls.length < changeFocusAt }) },
        runtime: { sendNativeMessage: async (host, request) => {
            calls.push({ host, request, title: page.title });
            const markerPresent = page.title.startsWith("BW-FX-" + request.nonce + " ");
            const base = { version: 1, windowId: request.windowId, nonce: request.nonce };
            const reply = request.command === "challenge-absent"
                ? { ...base, status: markerPresent ? "challenge-not-cleared" : "challenge-absent" }
                : { ...base, status: markerPresent ? "challenge-observed-not-authorized" : "challenge-missing",
                    browser: "firefox", handle: "0xABC", processId: 14, threadId: 15, processCreation: "123456789",
                    membership: { desktopId: "12345678-1234-1234-1234-123456789abc", isCurrent: true, desktopHResult: 0, currentHResult: 0 },
                    dpi: 144, screenBounds: { left: -750, top: 30, right: 600, bottom: 1080 }, frameBounds: null };
            return alter ? alter(reply, calls.length, request) : reply;
        } }
    };
    return { api, page, calls };
}

test("two different markers on the same native identity and verified cleanup", async () => {
    const { api, page, calls } = fixture();
    const result = await runChallenge(api, page, async () => {});
    assert.equal(result.status, "challenge-correlated-not-authorized");
    assert.equal(result.authorized, false);
    assert.equal(result.cleanup, "verified");
    assert.equal(page.title, title);
    assert.equal(calls.length, 7);
    const markers = calls.filter(c => c.request.command === "challenge-present");
    assert.equal(markers.length, 2);
    assert.notEqual(markers[0].request.nonce, markers[1].request.nonce);
    for (const call of calls) {
        assert.equal(call.host, "com.branchwatch.firefox_probe");
        assert.deepEqual(Object.keys(call.request).sort(), ["command","height","left","nonce","top","version","width","windowId"]);
    }
    assert.equal(calls.at(-1).title, title);
});

test("rejects ambiguous, missing and changed native candidates and resets page title", async () => {
    for (const status of ["challenge-ambiguous", "challenge-missing", "challenge-native-unverified", "challenge-changed"]) {
        const { api, page } = fixture({ alter: (r, n) => n === 3 ? { ...r, status } : r });
        await assert.rejects(() => runChallenge(api, page, async () => {}));
        assert.equal(page.title, title);
    }
});

test("rejects handle, process, thread, creation, desktop and bounds drift across challenges", async () => {
    for (const patch of [
        { handle: "0xABD" }, { processId: 99 }, { threadId: 98 }, { processCreation: "123456790" },
        { membership: { desktopId: "22345678-1234-1234-1234-123456789abc", isCurrent: true, desktopHResult: 0, currentHResult: 0 } },
        { screenBounds: { left: 0, top: 30, right: 600, bottom: 1080 } }
    ]) {
        const { api, page } = fixture({ alter: (r, n) => n === 4 ? { ...r, ...patch } : r });
        await assert.rejects(() => runChallenge(api, page, async () => {}), /identity changed/);
        assert.equal(page.title, title);
    }
});

test("rejects mismatch, incomplete identity and unsuccessful cleanup", async () => {
    for (const [at, patch] of [
        [3, { nonce: "bad" }], [3, { processCreation: "0" }],
        [3, { membership: { desktopId: "00000000-0000-0000-0000-000000000000", isCurrent: true, desktopHResult: 0, currentHResult: 0 } }],
        [1, { status: "challenge-not-cleared" }], [6, { status: "challenge-not-cleared" }]
    ]) {
        const { api, page } = fixture({ alter: (r, n) => n === at ? { ...r, ...patch } : r });
        await assert.rejects(() => runChallenge(api, page, async () => {}));
        assert.equal(page.title, title);
    }
});

test("rejects private or missing diagnostic tab and focus drift", async () => {
    for (const options of [{ missingTab: true }, { privateTab: true }, { changeFocusAt: 3 }]) {
        const { api, page } = fixture(options);
        await assert.rejects(() => runChallenge(api, page, async () => {}));
        assert.equal(page.title, title);
    }
});
