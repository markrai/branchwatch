const host = "com.branchwatch.firefox_probe";
const idPattern = /^[0-9a-f]{32}$/;
function randomId() {
    return Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("");
}
async function profileId(api) {
    const stored = await api.storage.local.get("profileId");
    if (typeof stored.profileId === "string" && idPattern.test(stored.profileId)) return stored.profileId;
    const id = randomId();
    await api.storage.local.set({ profileId: id });
    return id;
}
function windowObservation(w) {
    if (w.incognito !== false || w.type !== "normal" || w.state !== "normal" || !w.focused)
        throw new Error("Use a focused, normal, non-private Firefox window.");
    const { id, left, top, width, height } = w;
    if (![id, left, top, width, height].every(Number.isSafeInteger) || id < 0 || width <= 0 || height <= 0)
        throw new Error("Window geometry unavailable.");
    return { windowId: id, left, top, width, height };
}
async function checkConnection(api) {
    const profile = await profileId(api);
    const nonce = randomId();
    const reply = await api.runtime.sendNativeMessage(host, { version: 1, command: "hello", profileId: profile, nonce });
    if (reply?.version !== 1 || reply.profileId !== profile || reply.nonce !== nonce
        || !["connected", "unavailable"].includes(reply.status)) throw new Error("Invalid connection response.");
    return { status: reply.status, profileId: profile };
}
async function inspectWindow(api) {
    const before = windowObservation(await api.windows.getCurrent());
    const nonce = randomId();
    const reply = await api.runtime.sendNativeMessage(host, { version: 1, command: "observe", nonce, ...before });
    const after = windowObservation(await api.windows.getCurrent());
    if (JSON.stringify(before) !== JSON.stringify(after) || reply?.version !== 1
        || reply.windowId !== before.windowId || reply.nonce !== nonce)
        throw new Error("Window changed or reply identity mismatched.");
    if (!["foreground-candidate-not-authorized", "foreground-changed", "parent-unverified", "parent-changed", "dpi-context-unavailable"].includes(reply.status)
        || (reply.status === "foreground-candidate-not-authorized" && reply.browser !== "firefox"))
        throw new Error("Invalid diagnostic status.");
    return { browserObservation: before, nativeObservation: reply, authorized: false };
}
const connect = document.getElementById("connect");
const inspect = document.getElementById("inspect");
const result = document.getElementById("result");
const challenge = document.getElementById("challenge");
async function run(action) {
    connect.disabled = inspect.disabled = challenge.disabled = true;
    result.textContent = "Checking...";
    try { result.textContent = JSON.stringify(await action(browser), null, 2); }
    catch (error) { result.textContent = String(error.message ?? error); }
    finally { connect.disabled = inspect.disabled = challenge.disabled = false; }
}
connect.addEventListener("click", () => run(checkConnection));
inspect.addEventListener("click", () => run(inspectWindow));

async function openChallengeTab(api) {
    const source = windowObservation(await api.windows.getCurrent());
    await api.tabs.create({ windowId: source.windowId, url: api.runtime.getURL("challenge.html"), active: true });
    return { status: "diagnostic-tab-opened" };
}
challenge.addEventListener("click", () => run(openChallengeTab));
