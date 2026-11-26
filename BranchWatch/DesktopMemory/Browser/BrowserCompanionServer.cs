using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace BranchWatch;

internal sealed class BrowserCompanionServer(string pipeName) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    private bool _disposed;

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null) return;
        _listener = Task.Run(() => ListenAsync(_stop.Token));
    }

    private async Task ListenAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                deadline.CancelAfter(BrowserCompanionProtocol.Timeout);
                try
                {
                    var bytes = await BrowserCompanionProtocol.ReadAsync(pipe, deadline.Token, BrowserTabCapture.MaxFrame).ConfigureAwait(false);
                    using var doc = System.Text.Json.JsonDocument.Parse(bytes);
                    byte[] response;
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("restoreRequest", out _))
                    {
                        var envelope = BrowserRestoreProtocol.ParseEnvelope(bytes);
                        response = BrowserCompanionProtocol.Encode(BrowserRestoreQueue.Shared.Redeem(envelope));
                    }
                    else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("request", out _))
                    {
                        var envelope = BrowserTabCapture.ParseEnvelope(bytes);
                        var accepted = BrowserCaptureInbox.Shared.TryReceive(envelope);
                        response = BrowserCompanionProtocol.Encode(new CompanionReply(1, accepted ? "received" : "rejected", envelope.Request.ProfileId, envelope.Request.Nonce));
                    }
                    else
                    {
                        var request = BrowserCompanionProtocol.ParseHello(bytes);
                        response = BrowserCompanionProtocol.Encode(new CompanionReply(1, "connected", request.ProfileId, request.Nonce));
                    }
                    await BrowserCompanionProtocol.WriteAsync(pipe, response, deadline.Token, BrowserTabCapture.MaxFrame)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException
                    or InvalidOperationException or FormatException or OverflowException or OperationCanceledException)
                {
                    Trace.TraceInformation("Browser companion request ended: {0}", ex.GetType().Name);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Browser companion pipe unavailable: {0}", ex.GetType().Name);
                try { await Task.Delay(500, cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        if (_listener is null)
        {
            _stop.Dispose();
            return;
        }
        var listener = _listener;
        var stop = _stop;
        _ = listener.ContinueWith(task =>
        {
            if (task.IsFaulted) Trace.TraceWarning("Browser companion listener stopped: {0}", task.Exception?.GetType().Name);
            stop.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
