using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BranchWatch;

internal interface IStickyNotePassiveBody : IDisposable
{
    bool IsVisible { get; }
    string? LastError { get; }
    bool TryShow();
    bool VerifyAffinity();
    bool TryAlign();
    void UpdateContent(StickyNote note);
}

internal sealed class StickyNotePassiveBodyLifecycle(
    StickyNote note,
    Func<IStickyNotePassiveBody> create,
    Action presentationChanged,
    Action<string?> reportFailure) : IDisposable
{
    private IStickyNotePassiveBody? _body;
    private bool _enabled;
    private bool _failed;
    private bool _busy;
    private bool _disposed;
    private long _revision;

    internal bool IsEnabled => _enabled && !_disposed;
    internal bool IsVisible => _body is { IsVisible: true };
    internal void Enable()
    {
        if (!_disposed) _enabled = true;
    }

    internal void ResetFailure()
    {
        if (!_disposed) _failed = false;
    }

    internal void Refresh(bool eligible, bool layoutReady)
    {
        if (!IsEnabled) return;
        if (!eligible)
        {
            Remove();
            return;
        }
        if (_busy || _failed || !layoutReady) return;
        _busy = true;
        try
        {
            if (_body is { } existing)
            {
                if (!existing.IsVisible || !existing.VerifyAffinity())
                {
                    Remove();
                    return;
                }
                AlignCore(existing);
                if (ReferenceEquals(_body, existing)) existing.UpdateContent(note);
                return;
            }

            CreateBody();
        }
        catch (Exception ex) when (IsWindowFailure(ex))
        {
            Fail(ex.GetType().Name);
        }
        finally { _busy = false; }
    }

    private void CreateBody()
    {
        var revision = _revision;
        IStickyNotePassiveBody? candidate = null;
        try
        {
            candidate = create();
            if (_disposed || revision != _revision) return;
            var shown = candidate.TryShow();
            if (_disposed || revision != _revision) return;
            if (!shown || !candidate.IsVisible)
            {
                var reason = candidate.LastError;
                var rejected = candidate;
                candidate = null;
                rejected.Dispose();
                if (revision == _revision) Fail(reason);
                return;
            }

            _body = candidate;
            candidate = null;
            presentationChanged();
            if (_body is { } attached)
            {
                AlignCore(attached);
                if (ReferenceEquals(_body, attached)) attached.UpdateContent(note);
            }
        }
        finally { candidate?.Dispose(); }
    }

    internal void Align(bool eligible, bool layoutReady)
    {
        if (!IsEnabled || _busy || !eligible || !layoutReady || _body is not { } body) return;
        _busy = true;
        try { AlignCore(body); }
        catch (Exception ex) when (IsWindowFailure(ex)) { Fail(ex.GetType().Name); }
        finally { _busy = false; }
    }

    private void AlignCore(IStickyNotePassiveBody body)
    {
        if (!ReferenceEquals(_body, body)) return;
        if (!body.IsVisible)
        {
            Remove();
            return;
        }
        if (body.TryAlign() || !ReferenceEquals(_body, body)) return;
        var reason = body.LastError;
        if (!body.VerifyAffinity())
        {
            Remove();
            return;
        }
        Fail(reason);
    }

    internal void UpdateContent()
    {
        if (_disposed || _body is not { } body) return;
        try { body.UpdateContent(note); }
        catch (Exception ex) when (IsWindowFailure(ex)) { Fail(ex.GetType().Name); }
    }

    internal void Remove()
    {
        _revision++;
        var body = _body;
        _body = null;
        if (body is null) return;
        try { body.Dispose(); }
        finally
        {
            if (!_disposed) presentationChanged();
        }
    }

    private void Fail(string? reason)
    {
        if (_disposed) return;
        _failed = true;
        Remove();
        reportFailure(reason);
    }

    private static bool IsWindowFailure(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or Win32Exception or COMException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Remove();
    }
}
