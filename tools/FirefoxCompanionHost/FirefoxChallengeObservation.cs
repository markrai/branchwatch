using System.Runtime.InteropServices;
using System.Text;
using static BranchWatch.ProbeNative;

namespace BranchWatch;

internal static class FirefoxChallengeObservation
{
    private const int MaxWindows = 4096;
    internal static string Marker(string nonce) => $"BW-FX-{nonce} ";

    internal static bool HasMarker(string title, string nonce) =>
        title.StartsWith(Marker(nonce), StringComparison.Ordinal);

    internal static BrowserProbeResponse Inspect(BrowserProbeRequest request, bool present)
    {
        BrowserProbeResponse Result(string status) => new(1, request.WindowId, request.Nonce, status);
        var first = Scan(request.Nonce);
        if (first is null) return Result("challenge-scan-unverified");
        if (!present)
        {
            var second = Scan(request.Nonce);
            return first.Length == 0 && second is { Length: 0 }
                ? Result("challenge-absent") : Result("challenge-not-cleared");
        }

        if (first.Length != 1) return Result(first.Length == 0 ? "challenge-missing" : "challenge-ambiguous");
        var handle = first[0];
        if (GetForegroundWindow() != handle) return Result("challenge-not-foreground");
        var observation = NativeObservation.Inspect(request, handle, firefoxDiagnostic: true);
        if (observation.Status != "foreground-candidate-not-authorized") return Result("challenge-native-unverified");
        var after = Scan(request.Nonce);
        if (after is not { Length: 1 } || after[0] != handle || GetForegroundWindow() != handle)
            return Result("challenge-changed");
        var verified = NativeObservation.Inspect(request, handle, firefoxDiagnostic: true);
        if (verified != observation || GetForegroundWindow() != handle || !Matches(handle, request.Nonce))
            return Result("challenge-changed");
        return observation with { Status = "challenge-observed-not-authorized" };
    }

    private static IntPtr[]? Scan(string nonce)
    {
        var count = 0;
        var matches = new List<IntPtr>();
        var complete = EnumWindows((window, _) =>
        {
            if (++count > MaxWindows) return false;
            if (Matches(window, nonce)) matches.Add(window);
            return matches.Count <= 1;
        }, IntPtr.Zero);
        return matches.Count > 1 ? matches.ToArray() : complete ? matches.ToArray() : null;
    }

    private static bool Matches(IntPtr handle, string nonce)
    {
        var name = new StringBuilder(256);
        if (GetClassName(handle, name, name.Capacity) <= 0 || name.ToString() != "MozillaWindowClass") return false;
        var title = new StringBuilder(128);
        return GetWindowText(handle, title, title.Capacity) > 0 && HasMarker(title.ToString(), nonce);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
