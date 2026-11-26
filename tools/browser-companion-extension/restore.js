function validRestoreTabs(tabs) {
    if (!Array.isArray(tabs) || tabs.length < 1 || tabs.length > 100) return false;
    let unpinned = false;
    let active = 0;
    return tabs.every(tab => {
        if (!tab || typeof tab.url !== "string" || tab.url.length > 2048 || /\s/.test(tab.url)
            || typeof tab.pinned !== "boolean" || typeof tab.active !== "boolean") return false;
        let url;
        try { url = new URL(tab.url); } catch { return false; }
        if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) return false;
        if (!tab.pinned) unpinned = true;
        else if (unpinned) return false;
        if (tab.active) active++;
        return active <= 1;
    });
}

function sameUrl(actual, expected) {
    try { return new URL(actual).href === new URL(expected).href; } catch { return false; }
}

const maxInventoryWindows = 128;
const maxInventoryTabs = 4096;

function canonicalUrl(value) {
    if (typeof value !== "string" || !value.length) return null;
    try { return new URL(value).href; } catch { return null; }
}

async function inspectProfile(api) {
    const windows = await api.windows.getAll({ windowTypes: ["normal"], populate: true });
    if (!Array.isArray(windows) || windows.length > maxInventoryWindows) return null;
    const result = [];
    const windowIds = new Set();
    const tabIds = new Set();
    let count = 0;
    for (const w of windows) {
        if (w.incognito === true) continue;
        if (w.incognito !== false || w.type !== "normal" || !Number.isSafeInteger(w.id) || w.id < 0
            || windowIds.has(w.id) || !Array.isArray(w.tabs) || !w.tabs.length) return null;
        windowIds.add(w.id);
        count += w.tabs.length;
        if (count > maxInventoryTabs) return null;
        const ordered = [...w.tabs].sort((a,b) => a.index - b.index);
        const tabs = [];
        for (const [index, t] of ordered.entries()) {
            if (t.incognito !== false || t.windowId !== w.id || !Number.isSafeInteger(t.id) || t.id < 0
                || tabIds.has(t.id) || t.index !== index || typeof t.pinned !== "boolean"
                || typeof t.active !== "boolean" || t.pendingUrl) return null;
            const url = canonicalUrl(t.url);
            if (!url || url.length > 16384) return null;
            tabIds.add(t.id);
            tabs.push({ id: t.id, url, pinned: t.pinned, active: t.active });
        }
        if (tabs.filter(t => t.active).length !== 1) return null;
        result.push({ id: w.id, state: w.state, focused: w.focused === true, tabs });
    }
    return result.sort((a,b) => a.id - b.id);
}

function reconcileProfile(inventory, sourceWindowId, desired) {
    if (!inventory || !validRestoreTabs(desired)) return { status: "inventory-unverified" };
    const source = inventory.find(w => w.id === sourceWindowId);
    if (!source || source.state !== "normal" || !source.focused) return { status: "inventory-unverified" };
    const urls = desired.map(t => canonicalUrl(t.url));
    const wanted = new Set(urls);
    const active = Math.max(0, desired.findIndex(t => t.active));
    const exact = w => w.tabs.length === desired.length && w.tabs.every((t,i) =>
        t.url === urls[i] && t.pinned === desired[i].pinned && t.active === (i === active));
    const matches = inventory.filter(exact);
    const overlapping = inventory.filter(w => w.tabs.some(t => wanted.has(t.url)));
    if (matches.length === 1 && matches[0].id === sourceWindowId && overlapping.length === 1)
        return { status: "already-present", count: desired.length };
    if (overlapping.length > 0)
        return { status: "overlap", windows: overlapping.length };
    return { status: "clear" };
}

async function checkProfileBeforeCreate(api, sourceWindowId, tabs) {
    try {
        const first = await inspectProfile(api);
        const second = await inspectProfile(api);
        if (!first || !second || JSON.stringify(first) !== JSON.stringify(second))
            return { status: "inventory-unverified" };
        return reconcileProfile(second, sourceWindowId, tabs);
    } catch {
        return { status: "inventory-unverified" };
    }
}

async function reopenSavedTabs(api, sourceWindowId, tabs) {
    if (!validRestoreTabs(tabs)) return { status: "rejected" };
    let created = false;
    try {
        const source = await api.windows.get(sourceWindowId);
        if (source.id !== sourceWindowId || source.incognito !== false || source.type !== "normal"
            || source.state !== "normal" || !source.focused) return { status: "rejected" };
        if (!await api.permissions.contains({ permissions: ["tabs"] })) return { status: "rejected" };
        const reconciliation = await checkProfileBeforeCreate(api, sourceWindowId, tabs);
        if (reconciliation.status !== "clear") return reconciliation;
        const current = await api.windows.get(sourceWindowId);
        if (current.id !== sourceWindowId || current.incognito !== false || current.type !== "normal"
            || current.state !== "normal" || !current.focused
            || !await api.permissions.contains({ permissions: ["tabs"] })) return { status: "inventory-unverified" };
        const win = await api.windows.create({ url: tabs.map(t => t.url), type: "normal", incognito: false, focused: false });
        created = true;
        if (!Number.isSafeInteger(win?.id) || win.id === sourceWindowId || win.incognito !== false) return { status: "partial" };
        const ordered = (await api.tabs.query({ windowId: win.id })).sort((a,b) => a.index - b.index);
        if (ordered.length !== tabs.length || new Set(ordered.map(t => t.id)).size !== tabs.length
            || ordered.some((t,i) => !Number.isSafeInteger(t.id) || t.windowId !== win.id || t.incognito !== false
                || !sameUrl(t.pendingUrl ?? t.url, tabs[i].url))) return { status: "partial" };
        async function update(i, properties) {
            const current = await api.tabs.get(ordered[i].id);
            if (current.windowId !== win.id || current.incognito !== false) throw new Error("Tab moved");
            await api.tabs.update(ordered[i].id, properties);
        }
        for (let i = 0; i < tabs.length; i++) {
            if (tabs[i].pinned !== ordered[i].pinned) await update(i, { pinned: tabs[i].pinned });
        }
        const selected = tabs.findIndex(t => t.active);
        await update(selected < 0 ? 0 : selected, { active: true });
        const final = (await api.tabs.query({ windowId: win.id })).sort((a,b) => a.index - b.index);
        if (final.length !== tabs.length || final.some((t,i) => t.id !== ordered[i].id || t.windowId !== win.id
            || t.pinned !== tabs[i].pinned || t.active !== (i === Math.max(0, selected)))) return { status: "partial" };
        return { status: "opened", count: tabs.length };
    } catch {
        return { status: created ? "partial" : "uncertain" };
    }
}
