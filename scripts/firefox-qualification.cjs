const fs = require("node:fs");
const assert = require("node:assert/strict");
const crypto = require("node:crypto");

function utc(value) {
    assert(typeof value === "string" && /(?:Z|[+-]\d\d:\d\d)$/.test(value), "Missing offset-aware timestamp");
    const time = Date.parse(value);
    assert(Number.isFinite(time), "Invalid timestamp");
    return time;
}
function bounds(rect, native = false) {
    assert(rect && typeof rect === "object", "Missing bounds");
    const keys = native ? ["Left", "Top", "Right", "Bottom"] : ["left", "top", "right", "bottom"];
    const result = keys.map(k => rect[k]);
    assert(result.every(Number.isSafeInteger) && result[2] > result[0] && result[3] > result[1], "Invalid bounds");
    return result;
}
function guid(value) {
    assert(typeof value === "string" && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
        && value !== "00000000-0000-0000-0000-000000000000", "Invalid desktop identity");
    return value.toLowerCase();
}
function nativeIdentity(reply) {
    assert(reply.browser === "firefox" && /^0x[0-9A-F]+$/.test(reply.handle) && reply.handle !== "0x0", "Invalid native handle");
    assert(Number.isSafeInteger(reply.processId) && reply.processId > 0 && Number.isSafeInteger(reply.threadId) && reply.threadId > 0
        && typeof reply.processCreation === "string" && /^[1-9][0-9]*$/.test(reply.processCreation), "Invalid native process identity");
    assert(reply.membership?.isCurrent === true && Number.isInteger(reply.membership.desktopHResult) && reply.membership.desktopHResult >= 0
        && Number.isInteger(reply.membership.currentHResult) && reply.membership.currentHResult >= 0, "Unverified desktop membership");
    assert(Number.isSafeInteger(reply.dpi) && reply.dpi > 0, "Invalid DPI");
    return [reply.handle, reply.processId, reply.threadId, reply.processCreation, guid(reply.membership.desktopId),
        reply.dpi, bounds(reply.screenBounds), reply.frameBounds == null ? null : bounds(reply.frameBounds)];
}
function verifyProbe(report, label, identity) {
    assert(report.SchemaVersion === 1 && report.IncludesIdentities === true && report.PerMonitorDpiContext === true
        && report.TopologyStable === true && report.DesktopHintStable === true, "Independent probe incomplete or unstable");
    const start = utc(report.StartedUtc);
    const finish = utc(report.FinishedUtc);
    assert(start <= finish, "Probe timestamps reversed");
    assert(guid(report.DesktopsBefore?.ActiveDesktopHint) === identity[4]
        && guid(report.DesktopsAfter?.ActiveDesktopHint) === identity[4], "Independent active desktop mismatch");
    assert(Array.isArray(report.Windows) && Array.isArray(report.MonitorsAfter) && report.MonitorsAfter.length > 0,
        "Independent window or monitor inventory missing");
    const matches = report.Windows.filter(w => typeof w.Title === "string" && (w.Title === label || w.Title.startsWith(label + " ")));
    assert(matches.length === 1, "Independent label missing or ambiguous");
    const w = matches[0];
    assert(w.ClassName === "MozillaWindowClass" && typeof w.ExecutablePath === "string" && /(?:^|[\\/])firefox\.exe$/i.test(w.ExecutablePath)
        && w.Classification === "candidate" && w.Visible === true && w.Owned === false && w.ToolWindow === false
        && w.Minimized === false && w.Maximized === false && w.CloakFlags === 0 && w.CloakHResult >= 0
        && w.Membership?.IsCurrent === true && w.Membership.DesktopHResult >= 0 && w.Membership.CurrentHResult >= 0,
        "Independent window is not a verified normal Firefox candidate");
    assert(w.Handle === identity[0] && w.ProcessId === identity[1] && guid(w.Membership.DesktopId) === identity[4]
        && w.WindowDpi === identity[5], "Independent native identity mismatch");
    assert.deepEqual(bounds(w.ScreenBounds, true), identity[6], "Independent window bounds mismatch");
    if (identity[7] !== null) {
        assert(w.FrameHResult >= 0, "Independent frame unavailable");
        assert.deepEqual(bounds(w.VisibleFrameBounds, true), identity[7], "Independent frame mismatch");
    }
    assert(!report.Windows.some(x => typeof x.Title === "string" && /^BW-FX-[0-9a-f]{32} /.test(x.Title)),
        "Residual diagnostic marker found in independent probe");
    return { start, finish };
}
function verify(challenge, before, after) {
    assert(challenge.schemaVersion === 1 && challenge.status === "challenge-correlated-not-authorized"
        && challenge.authorized === false && challenge.cleanup === "verified", "Not a successful diagnostic report");
    const label = challenge.diagnosticTitle;
    assert(typeof label === "string" && /^BranchWatch Firefox qualification [A-Za-z0-9-]{1,32}$/.test(label),
        "Set a unique qualification label before testing");
    assert(Number.isSafeInteger(challenge.browserWindowId) && challenge.browserWindowId >= 0, "Invalid browser window ID");
    const start = utc(challenge.startedUtc), finish = utc(challenge.finishedUtc);
    assert(start <= finish && finish - start <= 120000, "Invalid challenge duration");
    const transcript = challenge.transcript;
    assert(Array.isArray(transcript) && transcript.length === 7, "Incomplete challenge transcript");
    const a = transcript[0].nonce, b = transcript[1].nonce;
    assert(typeof a === "string" && typeof b === "string" && /^[0-9a-f]{32}$/.test(a) && /^[0-9a-f]{32}$/.test(b) && a !== b,
        "Invalid or reused challenge markers");
    const expected = [
        ["challenge-absent", a], ["challenge-absent", b], ["challenge-present", a], ["challenge-present", b],
        ["challenge-absent", a], ["challenge-absent", a], ["challenge-absent", b]
    ];
    for (const [i, item] of transcript.entries()) {
        const [command, nonce] = expected[i];
        assert(item.command === command && item.nonce === nonce && item.reply?.version === 1
            && item.reply.nonce === nonce && item.reply.windowId === challenge.browserWindowId, "Transcript identity mismatch");
        assert(item.reply.status === (command === "challenge-present" ? "challenge-observed-not-authorized" : "challenge-absent"),
            "Unexpected marker result");
    }
    const identity = nativeIdentity(transcript[2].reply);
    assert.deepEqual(nativeIdentity(transcript[3].reply), identity, "Marker native identity changed");
    assert.deepEqual(challenge.nativeObservation, transcript[3].reply, "Summary differs from transcript");
    const prior = verifyProbe(before, label, identity);
    const later = verifyProbe(after, label, identity);
    assert(prior.finish <= start && finish <= later.start && later.finish - prior.start <= 15 * 60000,
        "Independent probes must bracket the challenge within 15 minutes");
    return { status: "CORRELATION_EVIDENCE_MATCH", authorized: false, physicalQualification: "manual-review-required" };
}

function main(args) {
    if (args.length !== 3) throw new Error("Usage: node scripts/firefox-qualification.cjs challenge.json before.probe.json after.probe.json");
    const inputs = args.map(path => {
        const bytes = fs.readFileSync(path);
        if (bytes.length > 16 * 1024 * 1024) throw new Error("Evidence file exceeds limit");
        return { hash: crypto.createHash("sha256").update(bytes).digest("hex"), data: JSON.parse(bytes.toString("utf8").replace(/^\uFEFF/, "")) };
    });
    assert(new Set(inputs.map(i => i.hash)).size === 3, "Evidence files must be distinct");
    const result = verify(...inputs.map(i => i.data));
    console.log(JSON.stringify({ ...result, sha256: inputs.map(i => i.hash) }, null, 2));
}

if (require.main === module) {
    try { main(process.argv.slice(2)); }
    catch (error) { console.error("EVIDENCE REJECTED: " + error.message); process.exitCode = 1; }
}
module.exports = { verify };
