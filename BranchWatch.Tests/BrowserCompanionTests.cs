using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace BranchWatch.Tests;

[TestClass]
public sealed class BrowserCompanionTests
{
    private static readonly CompanionHello Hello = new(1, "hello", "0123456789abcdef0123456789abcdef",
        "fedcba9876543210fedcba9876543210");

    [TestMethod]
    public void Protocol_HelloRoundTripsAndReplyMustMatch()
    {
        Assert.AreEqual(Hello, BrowserCompanionProtocol.ParseHello(BrowserCompanionProtocol.Encode(Hello)));
        var reply = new CompanionReply(1, "connected", Hello.ProfileId, Hello.Nonce);
        Assert.AreEqual(reply, BrowserCompanionProtocol.ParseReply(BrowserCompanionProtocol.Encode(reply), Hello));
        foreach (var invalid in new[]
        {
            reply with { Version = 2 },
            reply with { Status = "unavailable" },
            reply with { Nonce = Hello.ProfileId },
            reply with { ProfileId = Hello.Nonce }
        })
            Assert.ThrowsExactly<InvalidDataException>(() =>
                BrowserCompanionProtocol.ParseReply(BrowserCompanionProtocol.Encode(invalid), Hello));
    }

    [TestMethod]
    public void Protocol_RejectsCommandsAndExtraOrDuplicateFields()
    {
        var valid = Encoding.UTF8.GetString(BrowserCompanionProtocol.Encode(Hello));
        foreach (var malformed in new[]
        {
            valid.Replace("\"hello\"", "\"restore\""),
            valid.Replace("\"version\":1", "\"version\":2"),
            valid.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            valid.Replace("\"version\":1", "\"version\":1,\"url\":\"https://example.com\""),
            valid.Replace(Hello.ProfileId, "not-a-profile"),
            "[]"
        })
            Assert.ThrowsExactly<InvalidDataException>(() => BrowserCompanionProtocol.ParseHello(Encoding.UTF8.GetBytes(malformed)));
    }

    [TestMethod]
    public async Task Protocol_FrameRoundTripsAndRejectsOversizedOrTruncatedInput()
    {
        using var stream = new MemoryStream();
        var bytes = BrowserCompanionProtocol.Encode(Hello);
        await BrowserCompanionProtocol.WriteAsync(stream, bytes, default);
        stream.Position = 0;
        CollectionAssert.AreEqual(bytes, await BrowserCompanionProtocol.ReadAsync(stream, default));
        using var oversized = new MemoryStream([1, 4, 0, 0]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => BrowserCompanionProtocol.ReadAsync(oversized, default));
        using var truncated = new MemoryStream([2, 0, 0, 0, 1]);
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => BrowserCompanionProtocol.ReadAsync(truncated, default));
    }

    [TestMethod]
    public async Task Server_AcceptsHelloAfterMalformedClientAndDoesNotShareProfileState()
    {
        var name = $"BranchWatch-Browser-Test-{Guid.NewGuid():N}";
        using var server = new BrowserCompanionServer(name);
        server.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var malformed = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await malformed.ConnectAsync(timeout.Token);
            await BrowserCompanionProtocol.WriteAsync(malformed, Encoding.UTF8.GetBytes("""{"command":"restore"}"""), timeout.Token);
            var buffer = new byte[1];
            Assert.AreEqual(0, await malformed.ReadAsync(buffer, timeout.Token));
        }
        var first = await Connect(name, Hello, timeout.Token);
        Assert.AreEqual(Hello.ProfileId, first.ProfileId);
        var other = Hello with { ProfileId = Hello.Nonce, Nonce = Hello.ProfileId };
        var second = await Connect(name, other, timeout.Token);
        Assert.AreEqual(other.ProfileId, second.ProfileId);
        Assert.AreEqual(other.Nonce, second.Nonce);
    }

    private static async Task<CompanionReply> Connect(string name, CompanionHello request, CancellationToken cancellation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await BrowserCompanionProtocol.ConnectAsync(name, request, cancellation); }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(50, cancellation);
            }
        }
    }
}
