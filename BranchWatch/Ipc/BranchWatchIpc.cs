using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace BranchWatch;

internal sealed class BranchWatchIpcServer : IDisposable
{
    private readonly RepositorySessionController _sessionController;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenTask;

    public BranchWatchIpcServer(RepositorySessionController sessionController)
    {
        _sessionController = sessionController;
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cancellation.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                BranchWatchApplicationIdentity.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var requestJson = await ReadMessageAsync(pipe, _cancellation.Token).ConfigureAwait(false);
                var response = HandleRequest(requestJson);
                var responseJson = JsonSerializer.Serialize(response);
                await WriteMessageAsync(pipe, responseJson, _cancellation.Token).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private BranchWatchIpcResponse HandleRequest(string requestJson)
    {
        BranchWatchIpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BranchWatchIpcRequest>(requestJson);
        }
        catch
        {
            return BranchWatchIpcResponse.Failed("Invalid IPC request.");
        }

        if (request is null
            || !string.Equals(request.Command, "activity", StringComparison.OrdinalIgnoreCase))
        {
            return BranchWatchIpcResponse.Failed("Unsupported IPC command.");
        }

        if (!string.Equals(request.Reason, "repo-opened", StringComparison.OrdinalIgnoreCase))
        {
            return BranchWatchIpcResponse.Failed("Unsupported activity reason.");
        }

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return BranchWatchIpcResponse.Failed("Activity path is required.");
        }

        var result = _sessionController.TryReportRepoOpened(request.Path);
        return new BranchWatchIpcResponse(result.Success, result.Promoted, result.Message);
    }

    private static async Task<string> ReadMessageAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        await ReadExactAsync(pipe, lengthBuffer, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0 || length > 64 * 1024)
        {
            throw new InvalidDataException("Invalid IPC message length.");
        }

        var buffer = new byte[length];
        await ReadExactAsync(pipe, buffer, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task WriteMessageAsync(NamedPipeServerStream pipe, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var lengthBuffer = BitConverter.GetBytes(bytes.Length);
        await pipe.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
        await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}

internal static class BranchWatchIpcClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    public static BranchWatchIpcResponse? TrySendActivity(string path, string reason)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                BranchWatchApplicationIdentity.PipeName,
                PipeDirection.InOut,
                PipeOptions.None);

            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);

            var request = new BranchWatchIpcRequest
            {
                Command = "activity",
                Path = path,
                Reason = reason
            };
            var requestJson = JsonSerializer.Serialize(request);
            WriteMessage(pipe, requestJson);

            var responseJson = ReadMessage(pipe);
            return JsonSerializer.Deserialize<BranchWatchIpcResponse>(responseJson);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteMessage(NamedPipeClientStream pipe, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        pipe.Write(BitConverter.GetBytes(bytes.Length));
        pipe.Write(bytes);
        pipe.Flush();
    }

    private static string ReadMessage(NamedPipeClientStream pipe)
    {
        var lengthBuffer = new byte[4];
        ReadExact(pipe, lengthBuffer);
        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0 || length > 64 * 1024)
        {
            throw new InvalidDataException("Invalid IPC message length.");
        }

        var buffer = new byte[length];
        ReadExact(pipe, buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }
}

internal sealed class BranchWatchIpcRequest
{
    public string Command { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

internal sealed class BranchWatchIpcResponse
{
    public BranchWatchIpcResponse()
    {
    }

    public BranchWatchIpcResponse(bool success, bool promoted, string message)
    {
        Success = success;
        Promoted = promoted;
        Message = message;
    }

    public bool Success { get; set; }

    public bool Promoted { get; set; }

    public string Message { get; set; } = string.Empty;

    public static BranchWatchIpcResponse Failed(string message) =>
        new(false, false, message);
}
