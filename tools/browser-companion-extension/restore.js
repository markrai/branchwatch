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

async function reopenSavedTabs(api, sourceWindowId, tabs) {
    if (!validRestoreTabs(tabs)) return { status: "rejected" };
    let created = false;
    try {
        const source = await api.windows.get(sourceWindowId);
        if (source.id !== sourceWindowId || source.incognito !== false || source.type !== "normal"
            || source.state !== "normal" || !source.focused) return { status: "rejected" };
        if (!await api.permissions.contains({ permissions: ["tabs"] })) return { status: "rejected" };
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
