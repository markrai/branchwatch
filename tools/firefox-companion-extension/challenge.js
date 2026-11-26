const challengeHost = "com.branchwatch.firefox_probe";
const normalTitle = "BranchWatch Firefox correlation test";
function newNonce() {
    return Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("");
}
function stableIdentity(reply) {
    if (reply?.browser !== "firefox" || !/^0x[0-9A-F]+$/.test(reply.handle ?? "")
        || !Number.isSafeInteger(reply.processId) || reply.processId <= 0
        || !Number.isSafeInteger(reply.threadId) || reply.threadId <= 0
        || !/^[1-9][0-9]*$/.test(reply.processCreation ?? "")
        || !reply.membership?.desktopId || reply.membership.isCurrent !== true
        || !Number.isInteger(reply.membership.desktopHResult) || reply.membership.desktopHResult < 0
        || !Number.isInteger(reply.membership.currentHResult) || reply.membership.currentHResult < 0
        || !Number.isSafeInteger(reply.dpi) || reply.dpi <= 0
        || !reply.screenBounds) throw new Error("Native identity incomplete.");
    return JSON.stringify([reply.handle, reply.processId, reply.threadId, reply.processCreation,
        reply.membership.desktopId, reply.dpi, reply.screenBounds, reply.frameBounds]);
}
async function activeContext(api, expected) {
    const tab = await api.tabs.getCurrent();
    const w = await api.windows.getCurrent();
    const active = await api.tabs.query({ windowId: w.id, active: true });
    if (!tab || !Number.isSafeInteger(tab.id) || !Number.isSafeInteger(w.id) || w.id < 0
        || tab.windowId !== w.id || tab.incognito !== false || w.incognito !== false
        || w.type !== "normal" || w.state !== "normal" || !w.focused
        || active.length !== 1 || active[0].id !== tab.id || active[0].windowId !== w.id
        || ![w.left,w.top,w.width,w.height].every(Number.isSafeInteger) || w.width <= 0 || w.height <= 0)
        throw new Error("Keep the diagnostic tab active in a focused normal Firefox window.");
    const context = { tabId: tab.id, windowId: w.id, left: w.left, top: w.top, width: w.width, height: w.height };
    if (expected && JSON.stringify(expected) !== JSON.stringify(context)) throw new Error("Diagnostic window changed.");
    return context;
}
async function runChallenge(api, page, settle = () => new Promise(resolve => setTimeout(resolve, 150))) {
    const context = await activeContext(api);
    const { tabId, ...geometry } = context;
    const nonces = [newNonce(), newNonce()];
    if (nonces[0] === nonces[1]) throw new Error("Challenge randomness unavailable.");
    async function ask(command, nonce) {
        await activeContext(api, context);
        const reply = await api.runtime.sendNativeMessage(challengeHost, { version: 1, command, nonce, ...geometry });
        await activeContext(api, context);
        if (reply?.version !== 1 || reply.windowId !== context.windowId || reply.nonce !== nonce)
            throw new Error("Challenge reply did not match.");
        return reply;
    }
    async function absent(nonce) {
        if ((await ask("challenge-absent", nonce)).status !== "challenge-absent")
            throw new Error("Challenge marker absence not verified.");
    }
    const observed = [];
    try {
        await absent(nonces[0]);
        await absent(nonces[1]);
        for (const nonce of nonces) {
            await activeContext(api, context);
            page.title = "BW-FX-" + nonce + " " + normalTitle;
            await settle();
            const reply = await ask("challenge-present", nonce);
            if (reply.status !== "challenge-observed-not-authorized") throw new Error("Native marker observation failed: " + reply.status);
            stableIdentity(reply);
            observed.push(reply);
        }
        if (stableIdentity(observed[0]) !== stableIdentity(observed[1])) throw new Error("Native window identity changed.");
        await absent(nonces[0]);
    } finally {
        page.title = normalTitle;
    }
    await settle();
    await absent(nonces[0]);
    await absent(nonces[1]);
    return { status: "challenge-correlated-not-authorized", authorized: false, browserWindowId: context.windowId,
        nativeObservation: observed[1], cleanup: "verified" };
}
const runButton = document.getElementById("run");
const output = document.getElementById("result");
runButton.addEventListener("click", async () => {
    runButton.disabled = true;
    output.textContent = "Checking...";
    try { output.textContent = JSON.stringify(await runChallenge(browser, document), null, 2); }
    catch (error) { output.textContent = "Not qualified: " + String(error.message ?? error) + ". Close this test tab if necessary."; }
    finally { runButton.disabled = false; }
});
