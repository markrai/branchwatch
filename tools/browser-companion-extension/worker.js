importScripts("restore.js");
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
        || !["checkConnection", "getProfileId", "reopenApproved", "getRestoreStatus"].includes(message?.command)) return false;
    const work = message.command === "checkConnection" ? checkConnection()
        : message.command === "getProfileId" ? getProfileId().then(profileId => ({ profileId }))
        : message.command === "getRestoreStatus" ? chrome.storage.session.get("restoreStatus").then(s => s.restoreStatus ?? { status: "not-started" })
        : executeApprovedRestore(message);
    work.then(sendResponse, () => sendResponse({ status: "unavailable" }));
    return true;
});


let restoring = false;
async function executeApprovedRestore(message) {
    if (restoring) return { status: "busy" };
    const { reply, nonce, sourceWindowId } = message;
    if (!idPattern.test(nonce ?? "") || !Number.isSafeInteger(sourceWindowId) || reply?.version !== 1
        || reply.status !== "authorized" || reply.nonce !== nonce || !validRestoreTabs(reply.tabs)) return { status: "rejected" };
    restoring = true;
    try {
        if (reply.profileId !== await getProfileId()) return { status: "rejected" };
        const { restoreStatus, issuedRestoreNonces = [] } = await chrome.storage.session.get(["restoreStatus", "issuedRestoreNonces"]);
        if (restoreStatus?.status === "in-progress" || issuedRestoreNonces.includes(nonce)) return { status: "rejected" };
        if (issuedRestoreNonces.length >= 2048) return { status: "rejected" };
        await chrome.storage.session.set({ restoreStatus: { status: "in-progress", nonce }, issuedRestoreNonces: [...issuedRestoreNonces, nonce] });
        const result = await reopenSavedTabs(chrome, sourceWindowId, reply.tabs);
        await chrome.storage.session.set({ restoreStatus: { ...result, nonce } });
        return result;
    } catch {
        return { status: "uncertain" };
    } finally { restoring = false; }
}
