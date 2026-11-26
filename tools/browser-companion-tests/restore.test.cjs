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
function fixture({ focused = true, permission = true, moved = false, mismatch = false, failure = false, inventory, secondInventory, inventoryFailure = false, sourceChanged = false } = {}) {
    const calls = [];
    let live = [];
    const source = { id: 1, focused, incognito: false, type: "normal", state: "normal" };
    let scans = 0;
    let sourceReads = 0;
    const defaultInventory = [{ ...source, tabs: [{ id: 10, windowId: 1, incognito: false, index: 0, url: "chrome://newtab/", pinned: false, active: true }] }];
    return { calls, api: {
        windows: {
            get: async () => ({ ...source, focused: sourceChanged && ++sourceReads > 1 ? false : source.focused }),
            getAll: async () => {
                if (inventoryFailure) throw new Error("inventory unavailable");
                return structuredClone((++scans === 2 && secondInventory) || inventory || defaultInventory);
            },
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

function existingWindow(id, saved, { focused = id === 1, state = "normal", incognito = false } = {}) {
    return { id, focused, state, incognito, type: "normal", tabs: saved.map((tab, index) => ({
        ...tab, id: id * 1000 + index, windowId: id, incognito, index
    })) };
}
const blank = [{ url: "chrome://newtab/", pinned: false, active: true }];

test("recognizes an exact source session without opening or modifying tabs", async () => {
    const { api, calls } = fixture({ inventory: [existingWindow(1, tabs)] });
    const result = await reopenSavedTabs(api, 1, tabs);
    assert.equal(result.status, "already-present");
    assert.equal(result.count, 3);
    assert.equal(calls.length, 0);
});
test("blocks an exact match in another window without assuming its desktop", async () => {
    const { api, calls } = fixture({ inventory: [existingWindow(1, blank), existingWindow(2, tabs)] });
    const result = await reopenSavedTabs(api, 1, tabs);
    assert.equal(result.status, "overlap");
    assert.equal(result.windows, 1);
    assert.equal(calls.length, 0);
});
test("blocks partial, reordered, extra and ambiguous matches without changing existing tabs", async () => {
    const variants = [
        [existingWindow(1, [tabs[0], { ...blank[0] }])],
        [existingWindow(1, [tabs[0], tabs[2], tabs[1]])],
        [existingWindow(1, [...tabs, { ...blank[0], active: false }])],
        [existingWindow(1, tabs), existingWindow(2, tabs)],
        [existingWindow(1, tabs), existingWindow(2, [{ ...tabs[2], active: true }])]
    ];
    for (const inventory of variants) {
        const { api, calls } = fixture({ inventory });
        assert.equal((await reopenSavedTabs(api, 1, tabs)).status, "overlap");
        assert.equal(calls.length, 0);
    }
});
test("preserves multiplicity and pinned/active metadata when recognizing sessions", async () => {
    const variants = [
        [tabs[0], { ...tabs[2], active: true }],
        tabs.map((t,i) => ({ ...t, pinned: false, active: i === 1 })),
        tabs.map((t,i) => ({ ...t, active: i === 2 }))
    ];
    for (const live of variants) {
        const { api, calls } = fixture({ inventory: [existingWindow(1, live)] });
        assert.equal((await reopenSavedTabs(api, 1, tabs)).status, "overlap");
        assert.equal(calls.length, 0);
    }
});
test("blocks incomplete or changing inventories", async () => {
    const missing = existingWindow(1, blank);
    delete missing.tabs[0].url;
    const pending = existingWindow(1, blank);
    pending.tabs[0].pendingUrl = tabs[0].url;
    const source = existingWindow(1, blank);
    const variants = [
        { inventory: [missing] },
        { inventory: [pending] },
        { inventory: [] },
        { inventoryFailure: true },
        { inventory: [source], secondInventory: [source, existingWindow(2, tabs)] },
        { sourceChanged: true }
    ];
    for (const options of variants) {
        const { api, calls } = fixture(options);
        assert.equal((await reopenSavedTabs(api, 1, tabs)).status, "inventory-unverified");
        assert.equal(calls.length, 0);
    }
});
test("ignores private windows and preserves exact query and fragment distinctions", async () => {
    const { api, calls } = fixture({ inventory: [
        existingWindow(1, [{ url: "https://example.com/a?other=1#different", pinned: false, active: true }]),
        existingWindow(2, tabs, { incognito: true })
    ] });
    assert.equal((await reopenSavedTabs(api, 1, tabs)).status, "opened");
    assert.equal(calls[0][0], "create");
});
test("canonical URL equality detects normalized host and default port", async () => {
    const live = tabs.map(t => ({ ...t, url: t.url.replace("https://example.com", "https://EXAMPLE.COM:443") }));
    const { api, calls } = fixture({ inventory: [existingWindow(1, live)] });
    assert.equal((await reopenSavedTabs(api, 1, tabs)).status, "already-present");
    assert.equal(calls.length, 0);
});
