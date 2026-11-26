const button = document.getElementById("probe");
const result = document.getElementById("result");

function observation(w) {
    if (w.incognito !== false || w.type !== "normal" || w.state !== "normal" || !w.focused) {
        throw new Error("Use a focused, normal, non-private browser window.");
    }
    const { id, left, top, width, height } = w;
    if (![id, left, top, width, height].every(Number.isSafeInteger) || id < 0 || width <= 0 || height <= 0) {
        throw new Error("Browser window geometry is unavailable.");
    }
    return { windowId: id, left, top, width, height };
}

button.addEventListener("click", async () => {
    button.disabled = true;
    result.textContent = "Inspecting...";
    try {
        const before = observation(await chrome.windows.getCurrent());
        const nonce = Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("");
        const response = await chrome.runtime.sendNativeMessage("com.branchwatch.browser_probe", { version: 1, nonce, ...before });
        const after = observation(await chrome.windows.getCurrent());
        if (JSON.stringify(before) !== JSON.stringify(after) || response?.version !== 1
            || response.windowId !== before.windowId || response.nonce !== nonce) {
            throw new Error("Window changed or response identity did not match. Retry.");
        }
        result.textContent = JSON.stringify({ browserObservation: before, nativeObservation: response }, null, 2);
    } catch (error) {
        result.textContent = String(error.message ?? error);
    } finally {
        button.disabled = false;
    }
});
