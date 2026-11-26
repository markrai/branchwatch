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
