using System.Diagnostics;

namespace BranchWatch.Tests;

[TestClass]
public sealed class VirtualDesktopSessionKeyPathTests
{
    [TestMethod]
    public void Value_ResolvesOnceAcrossRepeatedAndConcurrentReads()
    {
        var nativeCalls = 0;
        var managedCalls = 0;
        var path = new VirtualDesktopSessionKeyPath(
            () => { Interlocked.Increment(ref nativeCalls); return 42u; },
            () => { Interlocked.Increment(ref managedCalls); return 99u; });
        const string expected = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\42\VirtualDesktops";

        Parallel.For(0, 64, _ => Assert.AreEqual(expected, path.Value));

        Assert.AreEqual(1, nativeCalls);
        Assert.AreEqual(0, managedCalls);
        Assert.AreSame(path.Value, path.Value);
    }

    [TestMethod]
    public void Value_AcceptsSessionZeroWithoutFallback()
    {
        var path = new VirtualDesktopSessionKeyPath(
            () => 0u,
            () => throw new InvalidOperationException("Unexpected managed fallback"));

        Assert.AreEqual(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\0\VirtualDesktops",
            path.Value);
    }

    [TestMethod]
    public void Value_CachesManagedFallbackAfterNativeFailure()
    {
        var nativeCalls = 0;
        var managedCalls = 0;
        var path = new VirtualDesktopSessionKeyPath(
            () => { nativeCalls++; return null; },
            () => { managedCalls++; return 17u; });

        Assert.AreEqual(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\17\VirtualDesktops",
            path.Value);
        Assert.AreSame(path.Value, path.Value);
        Assert.AreEqual(1, nativeCalls);
        Assert.AreEqual(1, managedCalls);
    }

    [TestMethod]
    public void Value_RetriesIfNeitherLookupSucceeds()
    {
        var nativeCalls = 0;
        var managedCalls = 0;
        var path = new VirtualDesktopSessionKeyPath(
            () => ++nativeCalls == 1 ? null : 23u,
            () => { managedCalls++; throw new InvalidOperationException("Session unavailable"); });

        Assert.ThrowsExactly<InvalidOperationException>(() => { _ = path.Value; });
        Assert.AreEqual(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\23\VirtualDesktops",
            path.Value);
        Assert.AreSame(path.Value, path.Value);
        Assert.AreEqual(2, nativeCalls);
        Assert.AreEqual(1, managedCalls);
    }

    [TestMethod]
    public void Value_MatchesActualWindowsProcessSession()
    {
        using var process = Process.GetCurrentProcess();
        var expected = FormattableString.Invariant(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\{process.SessionId}\VirtualDesktops");

        Assert.AreEqual(expected, new VirtualDesktopSessionKeyPath().Value);
    }
}
