const button = document.getElementById("check");
const status = document.getElementById("status");
button.addEventListener("click", async () => {
    button.disabled = true;
    status.textContent = "Checking...";
    try {
        const reply = await chrome.runtime.sendMessage({ command: "checkConnection" });
        status.textContent = reply?.status === "connected"
            ? "Connected to BranchWatch. Local profile ID: " + reply.profileId
            : "Unavailable. Check that BranchWatch is running and the native host is registered.";
    } catch {
        status.textContent = "Unavailable. Check the extension and native host registration.";
    } finally {
        button.disabled = false;
    }
});
