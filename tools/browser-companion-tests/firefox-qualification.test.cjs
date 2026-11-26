const assert = require("node:assert/strict");
const test = require("node:test");
const { verify } = require("../../scripts/firefox-qualification.cjs");
const desktop = "12345678-1234-1234-1234-123456789abc";
const label = "BranchWatch Firefox qualification window-A";
const a = "0123456789abcdef0123456789abcdef";
const b = "fedcba9876543210fedcba9876543210";
function fixture() {
    const observed = { browser: "firefox", handle: "0xABC", processId: 42, threadId: 43, processCreation: "123456789",
        membership: { desktopId: desktop, isCurrent: true, desktopHResult: 0, currentHResult: 0 }, dpi: 144,
        screenBounds: { left: -750, top: 30, right: 600, bottom: 1080 }, frameBounds: null };
    const transcript = [["absent",a],["absent",b],["present",a],["present",b],["absent",a],["absent",a],["absent",b]]
        .map(([kind,nonce]) => ({ command: "challenge-" + kind, nonce, reply: {
            version: 1, windowId: 7, nonce, status: kind === "present" ? "challenge-observed-not-authorized" : "challenge-absent",
            ...(kind === "present" ? structuredClone(observed) : {})
        }}));
    const challenge = { schemaVersion: 1, status: "challenge-correlated-not-authorized", authorized: false, cleanup: "verified",
        diagnosticTitle: label, startedUtc: "2026-11-26T12:01:00Z", finishedUtc: "2026-11-26T12:01:05Z",
        browserWindowId: 7, browserObservation: { windowId: 7, left: -500, top: 20, width: 900, height: 700 },
        transcript, nativeObservation: structuredClone(transcript[3].reply) };
    const report = { SchemaVersion: 1, IncludesIdentities: true, PerMonitorDpiContext: true, TopologyStable: true, DesktopHintStable: true,
        DesktopsBefore: { ActiveDesktopHint: desktop }, DesktopsAfter: { ActiveDesktopHint: desktop },
        MonitorsBefore: [{}], MonitorsAfter: [{}], Windows: [{
            Title: label + " — Mozilla Firefox", ClassName: "MozillaWindowClass", ExecutablePath: "C:/Program Files/Mozilla Firefox/firefox.exe",
            Classification: "candidate", Visible: true, Owned: false, ToolWindow: false, Minimized: false, Maximized: false,
            CloakFlags: 0, CloakHResult: 0, Handle: "0xABC", ProcessId: 42, WindowDpi: 144,
            Membership: { DesktopId: desktop, IsCurrent: true, DesktopHResult: 0, CurrentHResult: 0 },
            ScreenBounds: { Left: -750, Top: 30, Right: 600, Bottom: 1080 }
        }] };
    const before = { ...structuredClone(report), StartedUtc: "2026-11-26T12:00:00Z", FinishedUtc: "2026-11-26T12:00:01Z" };
    const after = { ...structuredClone(report), StartedUtc: "2026-11-26T12:02:00Z", FinishedUtc: "2026-11-26T12:02:01Z" };
    return { challenge, before, after };
}
test("matching independent evidence remains explicitly unauthorized", () => {
    const { challenge, before, after } = fixture();
    assert.deepEqual(verify(challenge, before, after), { status: "CORRELATION_EVIDENCE_MATCH", authorized: false, physicalQualification: "manual-review-required" });
});
test("rejects incomplete, altered or reused marker evidence", () => {
    for (const mutate of [
        c => c.transcript.pop(), c => c.transcript[1].nonce = a, c => c.transcript[3].reply.handle = "0xDEF",
        c => c.transcript[3].reply.processCreation = "987654", c => c.transcript[6].reply.status = "challenge-not-cleared",
        c => c.nativeObservation.threadId = 99, c => c.authorized = true, c => c.browserObservation.width = 0
    ]) {
        const { challenge, before, after } = fixture();
        mutate(challenge);
        assert.throws(() => verify(challenge, before, after));
    }
});
test("rejects independent identity drift, ambiguity and residual markers", () => {
    for (const mutate of [
        r => r.Windows[0].Handle = "0xDEF", r => r.Windows[0].ProcessId = 99, r => r.Windows[0].ScreenBounds.Left = 0,
        r => r.Windows[0].WindowDpi = 96, r => r.Windows[0].Membership.IsCurrent = false,
        r => r.Windows.push(structuredClone(r.Windows[0])), r => r.Windows.push({ Title: "BW-FX-" + a + " Firefox" }),
        r => r.Windows[0].Title = "wrong label", r => r.Windows[0].ExecutablePath = "C:/firefox.exe.bak",
        r => r.Windows[0].CloakFlags = 1, r => r.IncludesIdentities = false, r => r.TopologyStable = false
    ]) {
        const { challenge, before, after } = fixture();
        mutate(after);
        assert.throws(() => verify(challenge, before, after));
    }
});
test("requires bracketing chronological fresh probes", () => {
    for (const mutate of [
        (c,b,a) => a.StartedUtc = "2026-11-26T12:00:30Z",
        (c,b,a) => b.FinishedUtc = "2026-11-26T12:02:00Z",
        (c,b,a) => a.FinishedUtc = "2026-11-26T12:30:00Z",
        (c,b,a) => a.DesktopsAfter.ActiveDesktopHint = "22345678-1234-1234-1234-123456789abc"
    ]) {
        const { challenge, before, after } = fixture();
        mutate(challenge,before,after);
        assert.throws(() => verify(challenge,before,after));
    }
});
