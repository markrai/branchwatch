using System.Diagnostics;
using System.Security.Cryptography;

namespace BranchWatch;

internal sealed class BrowserCaptureInbox
{
    internal static BrowserCaptureInbox Shared { get; } = new();
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private BrowserCaptureTicket? _pending;
    private BrowserCaptureEnvelope? _received;
    private System.Threading.Timer? _expiry;

    internal BrowserCaptureTicket Arm(Guid desktopId, Guid windowId, string browser, BrowserNativeToken token)
    {
        if (!BrowserTabCapture.SupportsLiveSession(browser)) throw new InvalidOperationException("Browser capture is not qualified.");
        lock (_gate)
        {
            Clear();
            var ticket = new BrowserCaptureTicket(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                desktopId, windowId, browser, token, Stopwatch.GetTimestamp());
            _pending = ticket;
            _expiry = new System.Threading.Timer(_ => Cancel(ticket.Code), null, Lifetime, System.Threading.Timeout.InfiniteTimeSpan);
            return ticket;
        }
    }

    internal bool TryReceive(BrowserCaptureEnvelope envelope)
    {
        lock (_gate)
        {
            if (!BrowserTabCapture.SupportsLiveSession(envelope.Browser)
                || _pending is not { } ticket || Expired(ticket) || _received is not null
                || envelope.Request.Ticket != ticket.Code || envelope.Browser != ticket.Browser
                || envelope.DesktopId != ticket.DesktopId || envelope.Token != ticket.Token) return false;
            _received = envelope;
            return true;
        }
    }

    internal BrowserCaptureEnvelope? Take(string code)
    {
        lock (_gate)
        {
            if (_pending is not { } ticket || ticket.Code != code || _received is null) return null;
            var result = Expired(ticket) ? null : _received;
            Clear();
            return result;
        }
    }

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
        _received = null;
    }

    private static bool Expired(BrowserCaptureTicket ticket) => Stopwatch.GetElapsedTime(ticket.Started) >= Lifetime;
}
