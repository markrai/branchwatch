const button = document.getElementById("check");
const output = document.getElementById("status");
button.addEventListener("click", async () => {
    button.disabled = true;
    output.textContent = "Checking...";
    try {
        const reply = await chrome.runtime.sendMessage({ command: "checkConnection" });
        output.textContent = reply?.status === "connected"
            ? "Connected to BranchWatch. Local profile ID: " + reply.profileId
            : "Unavailable. Check that BranchWatch is running and the native host is registered.";
    } catch {
        output.textContent = "Unavailable. Check the extension and native host registration.";
    } finally {
        button.disabled = false;
    }
});


const captureButton = document.getElementById("capture");
const captureOutput = document.getElementById("captureStatus");
function eligibleWindow(w) {
    return w.incognito === false && w.type === "normal" && w.state === "normal" && w.focused && Number.isSafeInteger(w.id);
}
function tabsFor(tabs, windowId) {
    return tabs.filter(t => t.windowId === windowId && t.incognito === false).sort((a,b) => a.index - b.index);
}
function serializableTabs(tabs) {
    return tabs.filter(t => !t.pendingUrl && typeof t.url === "string" && t.url.length <= 2048 && !/\s/.test(t.url)
        && (() => { try { const u = new URL(t.url); return ["http:", "https:"].includes(u.protocol) && !u.username && !u.password; } catch { return false; } })())
        .map(t => ({ url: t.url, pinned: t.pinned === true, active: t.active === true }));
}
captureButton.addEventListener("click", async () => {
    const ticket = document.getElementById("ticket").value.trim();
    if (!/^[0-9a-f]{32}$/.test(ticket)) { captureOutput.textContent = "Enter the capture code from BranchWatch."; return; }
    captureButton.disabled = true;
    captureOutput.textContent = "Collecting tabs...";
    try {
        if (!await chrome.permissions.request({ permissions: ["tabs"] })) throw new Error("Tab permission was not granted.");
        const w = await chrome.windows.getCurrent();
        if (!eligibleWindow(w)) throw new Error("Use a focused, normal, non-private window.");
        const profile = await chrome.runtime.sendMessage({ command: "getProfileId" });
        if (!/^[0-9a-f]{32}$/.test(profile?.profileId ?? "")) throw new Error("Profile identity unavailable.");
        const first = tabsFor(await chrome.tabs.query({ windowId: w.id }), w.id);
        const tabs = serializableTabs(first);
        const second = tabsFor(await chrome.tabs.query({ windowId: w.id }), w.id);
        const after = await chrome.windows.getCurrent();
        if (!eligibleWindow(after) || after.id !== w.id || JSON.stringify(first.map(t => [t.id,t.url,t.pendingUrl,t.index,t.pinned,t.active])) !== JSON.stringify(second.map(t => [t.id,t.url,t.pendingUrl,t.index,t.pinned,t.active])))
            throw new Error("The browser window or its tabs changed. Retry.");
        if (!tabs.length || tabs.length > 100) throw new Error("Capture requires 1–100 eligible tabs.");
        const nonce = Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("");
        const reply = await chrome.runtime.sendNativeMessage("com.branchwatch.companion", {
            version: 1, command: "capture", profileId: profile.profileId, nonce, ticket,
            windowId: w.id, tabs, omitted: first.length - tabs.length
        });
        if (reply?.version !== 1 || reply.nonce !== nonce || reply.profileId !== profile.profileId || reply.status !== "received")
            throw new Error("Capture rejected. Check the code, native host and selected window.");
        document.getElementById("ticket").value = "";
        captureOutput.textContent = "Sent " + tabs.length + " tabs. Return to BranchWatch and import the draft. Nothing has been saved.";
    } catch (error) {
        captureOutput.textContent = String(error.message ?? error);
    } finally {
        captureButton.disabled = false;
    }
});
