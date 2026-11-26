const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const directory = path.join(__dirname, "../browser-companion-extension");
const context = vm.createContext({ URL, document: { getElementById: () => ({ addEventListener() {} }) },
    chrome: { runtime: { sendMessage: async () => ({ status: "not-started" }) } } });
for (const file of ["restore.js", "popup.js"]) vm.runInContext(fs.readFileSync(path.join(directory, file), "utf8"), context);
const { tabsFor, serializableTabs } = context;
const tabs = [
    { id: 11, index: 0, windowId: 7, incognito: false, url: "https://example.com/", pinned: true, active: false },
    { id: 12, index: 1, windowId: 7, incognito: false, url: "https://example.com/", pinned: false, active: true },
    { id: 13, index: 2, windowId: 7, incognito: false, url: "chrome://newtab/", pinned: false, active: false }
];
test("capture verifies complete identity before preserving eligible ordered duplicates", () => {
    const selected = serializableTabs(tabsFor([...tabs].reverse(), 7));
    assert.equal(JSON.stringify(selected), JSON.stringify([
        { url: tabs[0].url, pinned: true, active: false },
        { url: tabs[1].url, pinned: false, active: true }
    ]));
});
test("capture rejects mixed windows, private tabs, duplicate identities, missing metadata and index gaps", () => {
    for (const patch of [{ windowId: 8 }, { incognito: true }, { id: 11 }, { id: -1 }, { index: 3 },
        { url: undefined }, { pinned: undefined }, { active: false }])
        assert.throws(() => tabsFor([tabs[0], { ...tabs[1], ...patch }, tabs[2]], 7));
});
test("capture omits pending, non-http, credential-bearing and raw-control URLs", () => {
    for (const patch of [{ pendingUrl: "https://example.org/" }, { url: "https://user:pass@example.com/" },
        { url: "file:///secret" }, { url: "https://example.com/" + String.fromCharCode(0) }]) {
        const selected = serializableTabs(tabsFor([tabs[0], { ...tabs[1], ...patch }, tabs[2]], 7));
        assert.equal(selected.length, 1);
        assert.equal(selected[0].active, false);
    }
});
