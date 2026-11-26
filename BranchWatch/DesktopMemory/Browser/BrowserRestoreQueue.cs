using System.Diagnostics;
using System.Security.Cryptography;

namespace BranchWatch;

internal sealed record BrowserRestoreKey(Guid DesktopId, DateTimeOffset CapturedUtc, Guid WindowId);
internal sealed record BrowserRestoreApproval(string Code, BrowserRestoreKey Key, BrowserWindowContext Context, long Started);

internal sealed class BrowserRestoreQueue(Func<long>? clock = null)
{
    private readonly Func<long> _clock = clock ?? Stopwatch.GetTimestamp;
    internal static BrowserRestoreQueue Shared { get; } = new();
    private readonly object _gate = new();
    private readonly HashSet<BrowserRestoreKey> _attempted = [];
    private BrowserRestoreApproval? _pending;
    private Func<bool>? _stillCurrent;
    private System.Threading.Timer? _expiry;

    internal bool WasIssued(BrowserRestoreKey key)
    {
        lock (_gate) return _attempted.Contains(key);
    }

    internal BrowserRestoreApproval Arm(BrowserRestoreKey key, BrowserWindowContext context, Func<bool> stillCurrent)
    {
        if (key.DesktopId == Guid.Empty || key.WindowId == Guid.Empty || key.CapturedUtc == default
            || !BrowserRestoreProtocol.CanRestore(context)) throw new InvalidOperationException("Browser session cannot be restored.");
        lock (_gate)
        {
            if (_attempted.Contains(key)) throw new InvalidOperationException("This browser window was already issued a restore request in this BranchWatch session.");
            Clear();
            var approval = new BrowserRestoreApproval(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                key, context with { Tabs = context.Tabs.ToArray() }, _clock());
            _pending = approval;
            _stillCurrent = stillCurrent;
            _expiry = new System.Threading.Timer(_ => Cancel(approval.Code), null, BrowserAuthorizationPolicy.Lifetime, System.Threading.Timeout.InfiniteTimeSpan);
            return approval;
        }
    }

    internal BrowserRestoreReply Redeem(BrowserRestoreEnvelope envelope)
    {
        var request = envelope.RestoreRequest;
        BrowserRestoreReply Rejected() => new(BrowserRestoreProtocol.Version, "rejected", request.ProfileId, request.Nonce);
        BrowserRestoreApproval pending;
        Func<bool>? verify;
        lock (_gate)
        {
            if (_pending is not { } candidate || !Matches(candidate, envelope)) return Rejected();
            pending = candidate;
            verify = _stillCurrent;
        }

        bool current;
        try { current = verify?.Invoke() == true; }
        catch (Exception) { current = false; }

        lock (_gate)
        {
            if (!ReferenceEquals(_pending, pending)) return Rejected();
            if (!current || !Matches(pending, envelope))
            {
                Clear();
                return Rejected();
            }

            _attempted.Add(pending.Key);
            Clear();
            return new(BrowserRestoreProtocol.Version, "authorized", request.ProfileId, request.Nonce, pending.Context.Tabs);
        }
    }

    private bool Matches(BrowserRestoreApproval pending, BrowserRestoreEnvelope envelope) =>
        BrowserTabCapture.SupportsLiveSession(envelope.Browser)
        && !BrowserAuthorizationPolicy.Expired(pending.Started, _clock())
        && envelope.RestoreRequest.Ticket == pending.Code && envelope.DesktopId == pending.Key.DesktopId
        && envelope.Browser == pending.Context.Browser && envelope.RestoreRequest.ProfileId == pending.Context.ProfileId
        && !_attempted.Contains(pending.Key);

    internal void Cancel(string? code)
    {
        lock (_gate)
        {
            if (_pending?.Code == code) Clear();
        }
    }

    private void Clear()
    {
        _expiry?.Dispose();
        _expiry = null;
        _pending = null;
        _stillCurrent = null;
    }
}
