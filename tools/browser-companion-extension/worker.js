const host = "com.branchwatch.companion";
const idPattern = /^[0-9a-f]{32}$/;
const randomId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), b => b.toString(16).padStart(2, "0")).join("");
let profilePromise;

async function getProfileId() {
    if (!profilePromise) {
        profilePromise = (async () => {
            const { profileId } = await chrome.storage.local.get("profileId");
            if (typeof profileId === "string" && idPattern.test(profileId)) return profileId;
            const next = randomId();
            await chrome.storage.local.set({ profileId: next });
            return next;
        })().catch(error => {
            profilePromise = undefined;
            throw error;
        });
    }
    return profilePromise;
}

async function checkConnection() {
    const profileId = await getProfileId();
    const nonce = randomId();
    const reply = await chrome.runtime.sendNativeMessage(host, { version: 1, command: "hello", profileId, nonce });
    if (reply?.version !== 1 || reply.profileId !== profileId || reply.nonce !== nonce
        || !["connected", "unavailable"].includes(reply.status)) throw new Error("Invalid connection response.");
    return { status: reply.status, profileId };
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (sender.id !== chrome.runtime.id || sender.url !== chrome.runtime.getURL("popup.html")
        || !["checkConnection", "getProfileId"].includes(message?.command)) return false;
    const work = message.command === "checkConnection" ? checkConnection() : getProfileId().then(profileId => ({ profileId }));
    work.then(sendResponse, () => sendResponse({ status: "unavailable" }));
    return true;
});
