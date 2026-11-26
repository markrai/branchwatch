using System.Diagnostics;
using System.IO;

namespace BranchWatch.Tests;

[TestClass]
public sealed class BrowserRestoreTests
{
    private const string Profile = "0123456789abcdef0123456789abcdef";
    private const string Other = "fedcba9876543210fedcba9876543210";
    private static readonly BrowserSavedTab[] Tabs = [new("https://example.com/a", true, false), new("https://example.com/a", false, true)];
    private static BrowserWindowContext Context => new("chrome", Profile, Tabs);
    private static BrowserRestoreKey Key() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
    private static BrowserRestoreEnvelope Envelope(BrowserRestoreApproval approval) =>
        new(new(2, "restore", Profile, Other, approval.Code, 7), "chrome", approval.Key.DesktopId);

    [TestMethod]
    public void Queue_RejectsWrongProfileFamilyDesktopOrCodeAndConsumesOnce()
    {
        var queue = new BrowserRestoreQueue();
        var key = Key();
        var approval = queue.Arm(key, Context, () => true);
        var request = Envelope(approval);
        try
        {
            foreach (var wrong in new[]
            {
                request with { Browser = "edge" },
                request with { DesktopId = Guid.NewGuid() },
                request with { RestoreRequest = request.RestoreRequest with { ProfileId = Other } },
                request with { RestoreRequest = request.RestoreRequest with { Ticket = Other } }
            })
            {
                var rejected = queue.Redeem(wrong);
                Assert.AreEqual("rejected", rejected.Status);
                Assert.IsNull(rejected.Tabs);
                Assert.IsFalse(queue.WasIssued(key));
            }
            var reply = queue.Redeem(request);
            Assert.AreEqual("authorized", reply.Status);
            CollectionAssert.AreEqual(Tabs, reply.Tabs!.ToArray());
            Assert.IsTrue(queue.WasIssued(key));
            Assert.AreEqual("rejected", queue.Redeem(request).Status);
            Assert.ThrowsExactly<InvalidOperationException>(() => queue.Arm(key, Context, () => true));
        }
        finally { queue.Cancel(approval.Code); }
    }

    [TestMethod]
    public void Queue_CancelsExpiredStaleAndReplacedApprovals()
    {
        var now = Stopwatch.GetTimestamp();
        var queue = new BrowserRestoreQueue(() => now);
        var key = Key();
        var expired = queue.Arm(key, Context, () => true);
        now += Stopwatch.Frequency * 120;
        Assert.AreEqual("rejected", queue.Redeem(Envelope(expired)).Status);
        Assert.IsFalse(queue.WasIssued(key));
        var stale = queue.Arm(key, Context, () => false);
        Assert.AreEqual("rejected", queue.Redeem(Envelope(stale)).Status);
        var first = queue.Arm(key, Context, () => true);
        var second = queue.Arm(Key(), Context, () => true);
        try
        {
            Assert.AreEqual("rejected", queue.Redeem(Envelope(first)).Status);
            queue.Cancel(first.Code);
            queue.Cancel(second.Code);
            Assert.AreEqual("rejected", queue.Redeem(Envelope(second)).Status);
        }
        finally { queue.Cancel(second.Code); }
    }

    [TestMethod]
    public void Protocol_BlocksEmptyUnsafeAndImpossiblePinnedOrder()
    {
        Assert.IsTrue(BrowserRestoreProtocol.CanRestore(Context));
        Assert.IsFalse(BrowserRestoreProtocol.CanRestore(null));
        Assert.IsFalse(BrowserRestoreProtocol.CanRestore(Context with { Tabs = [] }));
        Assert.IsFalse(BrowserRestoreProtocol.CanRestore(Context with { Tabs = [Tabs[1], Tabs[0]] }));
        Assert.IsFalse(BrowserRestoreProtocol.CanRestore(Context with { Tabs = [new("file:///C:/secret", false, true)] }));
        Assert.IsTrue(BrowserRestoreProtocol.CanRestore(Context with { Tabs = [Tabs[0]] }));
    }

    [TestMethod]
    public void Protocol_RequiresExactRequestAndBoundReplyIdentity()
    {
        var request = new BrowserRestoreRequest(2, "restore", Profile, Other, Profile, 7);
        var envelope = new BrowserRestoreEnvelope(request, "chrome", Guid.NewGuid());
        Assert.AreEqual(request, BrowserRestoreProtocol.ParseRequest(BrowserCompanionProtocol.Encode(request)));
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseRequest(BrowserCompanionProtocol.Encode(request with { Version = 1 })));
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseEnvelope(BrowserCompanionProtocol.Encode(envelope with { RestoreRequest = request with { Version = 1 } })));
        Assert.AreEqual(envelope, BrowserRestoreProtocol.ParseEnvelope(BrowserCompanionProtocol.Encode(envelope)));
        var text = System.Text.Encoding.UTF8.GetString(BrowserCompanionProtocol.Encode(request));
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseRequest(
            System.Text.Encoding.UTF8.GetBytes(text.Replace("\"version\":2", "\"version\":2,\"version\":2"))));
        var reply = new BrowserRestoreReply(2, "authorized", Profile, Other, Tabs);
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseReply(BrowserCompanionProtocol.Encode(reply with { Version = 1 }), request));
        CollectionAssert.AreEqual(Tabs, BrowserRestoreProtocol.ParseReply(BrowserCompanionProtocol.Encode(reply), request).Tabs!.ToArray());
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseReply(
            BrowserCompanionProtocol.Encode(reply with { Nonce = Profile }), request));
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseReply(
            BrowserCompanionProtocol.Encode(reply with { Status = "rejected" }), request));
        Assert.ThrowsExactly<InvalidDataException>(() => BrowserRestoreProtocol.ParseReply(
            BrowserCompanionProtocol.Encode(reply with { Tabs = null }), request));
    }
}
