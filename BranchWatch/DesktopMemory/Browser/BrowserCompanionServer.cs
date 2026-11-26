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
                    var request = BrowserCompanionProtocol.ParseHello(
                        await BrowserCompanionProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false));
                    var response = new CompanionReply(1, "connected", request.ProfileId, request.Nonce);
                    await BrowserCompanionProtocol.WriteAsync(pipe, BrowserCompanionProtocol.Encode(response), deadline.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException
                    or InvalidOperationException or FormatException or OperationCanceledException)
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
