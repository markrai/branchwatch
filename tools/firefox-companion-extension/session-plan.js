const firefoxDefaultStore = "firefox-default";

function planFirefoxCapture(first, second, windowId, profileId, originalActiveId) {
    if (!Number.isSafeInteger(windowId) || windowId < 0 || !Number.isSafeInteger(originalActiveId)
        || originalActiveId < 0 || !/^[0-9a-f]{32}$/.test(profileId ?? "")) return null;
    function normalize(observation) {
        if (!Array.isArray(observation) || observation.length < 1 || observation.length > 4096) return null;
        const ids = new Set();
        const ordered = [...observation].sort((a,b) => a.index - b.index);
        const tabs = [];
        for (const [index, tab] of ordered.entries()) {
            if (!tab || tab.windowId !== windowId || tab.incognito !== false || tab.index !== index
                || !Number.isSafeInteger(tab.id) || tab.id < 0 || ids.has(tab.id)
                || typeof tab.pinned !== "boolean" || typeof tab.active !== "boolean"
                || typeof tab.cookieStoreId !== "string" || !tab.cookieStoreId
                || tab.pendingUrl || typeof tab.url !== "string" || !tab.url) return null;
            ids.add(tab.id);
            tabs.push({ id: tab.id, url: tab.url, cookieStoreId: tab.cookieStoreId,
                pinned: tab.pinned, active: tab.active });
        }
        if (tabs.filter(t => t.active).length !== 1 || !tabs.some(t => t.id === originalActiveId)) return null;
        return tabs;
    }
    const a = normalize(first);
    const b = normalize(second);
    if (!a || !b || JSON.stringify(a) !== JSON.stringify(b)) return null;
    const tabs = [];
    let omittedContainer = 0;
    let omittedUnsupported = 0;
    for (const tab of a) {
        if (tab.cookieStoreId !== firefoxDefaultStore) {
            omittedContainer++;
            continue;
        }
        let safe = false;
        if (tab.url.length <= 2048 && !/\s/.test(tab.url)) {
            try {
                const url = new URL(tab.url);
                safe = ["http:", "https:"].includes(url.protocol) && !!url.hostname && !url.username && !url.password;
            } catch {}
        }
        if (!safe) {
            omittedUnsupported++;
            continue;
        }
        tabs.push({ url: tab.url, pinned: tab.pinned, active: tab.id === originalActiveId });
    }
    if (tabs.length < 1 || tabs.length > 100) return null;
    let unpinned = false;
    for (const tab of tabs) {
        if (!tab.pinned) unpinned = true;
        else if (unpinned) return null;
    }
    const context = { browser: "firefox", profileId, tabs };
    if (new TextEncoder().encode(JSON.stringify(context)).length > 200 * 1024) return null;
    return { context, omittedContainer, omittedUnsupported };
}
