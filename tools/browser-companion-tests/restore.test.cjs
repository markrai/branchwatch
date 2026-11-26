const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const context = vm.createContext({ URL });
vm.runInContext(fs.readFileSync(path.join(__dirname, "../browser-companion-extension/restore.js"), "utf8"), context);
const { validRestoreTabs, reopenSavedTabs } = context;
const tabs = [
    { url: "https://example.com/a", pinned: true, active: false },
    { url: "https://example.com/a", pinned: false, active: true },
    { url: "https://example.com/b", pinned: false, active: false }
];
function fixture({ focused = true, permission = true, moved = false, mismatch = false, failure = false } = {}) {
    const calls = [];
    let live = [];
    const source = { id: 1, focused, incognito: false, type: "normal", state: "normal" };
    return { calls, api: {
        windows: {
            get: async () => source,
            create: async options => {
                calls.push(["create", options]);
                if (failure) throw new Error("uncertain creation");
                live = options.url.map((url, index) => ({
                    id: index + 20, windowId: 2, incognito: false, index, url: mismatch && index === 0 ? "https://example.org/" : url, pinned: false, active: index === 0
                }));
                return { id: 2, incognito: false };
            }
        },
        permissions: { contains: async () => permission },
        tabs: {
            query: async ({ windowId }) => live.filter(t => t.windowId === windowId).map(t => ({ ...t })),
            get: async id => ({ ...live.find(t => t.id === id), ...(moved ? { windowId: 3 } : {}) }),
            update: async (id, properties) => {
                calls.push(["update", id, properties]);
                if (properties.active) live.forEach(t => t.active = t.id === id);
                Object.assign(live.find(t => t.id === id), properties);
            }
        }
    } };
}
test("rejects unsafe URLs, empty lists and invalid pinned order", () => {
    assert.equal(validRestoreTabs(tabs), true);
    assert.equal(validRestoreTabs([]), false);
    assert.equal(validRestoreTabs([tabs[1], tabs[0]]), false);
    assert.equal(validRestoreTabs([{ ...tabs[0], url: "file:///secret" }]), false);
    assert.equal(validRestoreTabs([{ ...tabs[0], url: "https://user:pass@example.com" }]), false);
    assert.equal(validRestoreTabs([tabs[1], tabs[1]]), false);
});
test("opens duplicates in order and only updates newly created tabs", async () => {
    const { api, calls } = fixture();
    const result = await reopenSavedTabs(api, 1, tabs);
    assert.equal(result.status, "opened");
    assert.equal(result.count, 3);
    assert.equal(JSON.stringify(calls[0][1].url), JSON.stringify(tabs.map(t => t.url)));
    assert.equal(calls[0][1].incognito, false);
    assert.equal(calls[1][1], 20);
    assert.equal(calls[2][1], 21);
    assert.equal(calls.length, 3);
});
test("defaults to first retained tab if original active tab was excluded", async () => {
    const { api, calls } = fixture();
    const result = await reopenSavedTabs(api, 1, [tabs[0], tabs[2]]);
    assert.equal(result.status, "opened");
    assert.equal(calls.at(-1)[1], 20);
});
test("does not create if source focus or permission is absent", async () => {
    for (const options of [{ focused: false }, { permission: false }]) {
        const { api, calls } = fixture(options);
        assert.equal((await reopenSavedTabs(api, 1, tabs)).status, "rejected");
        assert.equal(calls.length, 0);
    }
});
test("reports partial or uncertain and never retries or closes anything", async () => {
    for (const [options, status] of [[{ moved: true }, "partial"], [{ mismatch: true }, "partial"], [{ failure: true }, "uncertain"]]) {
        const { api, calls } = fixture(options);
        assert.equal((await reopenSavedTabs(api, 1, tabs)).status, status);
        assert.equal(calls.length, 1);
    }
});
