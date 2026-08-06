using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.CoreLogs;

namespace Stelliberty.Infrastructure.Core;

internal sealed class CorePipeLogStreamer : IDisposable
{
    private const int MaxFrameBytes = 1024 * 1024;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly CoreLogParser _parser = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _streamCancellation;
    private Task? _streamTask;
    private bool _isDisposed;

    public CorePipeLogStreamer(string corePipe)
    {
        _pipeName = NormalizeEndpoint(corePipe);
    }

    public event EventHandler<CoreLogMessage>? MessageReceived;

    public void Restart()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            StopLocked();
            _streamCancellation = new CancellationTokenSource();
            _streamTask = Task.Run(() => RunAsync(_streamCancellation.Token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            StopLocked();
        }

        MessageReceived = null;
    }

    private void StopLocked()
    {
        var cancellation = _streamCancellation;
        var task = _streamTask;
        _streamCancellation = null;
        _streamTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        DisposeCancellationAfterTask(cancellation, task);
    }

    private static void DisposeCancellationAfterTask(CancellationTokenSource cancellation, Task? task)
    {
        if (task is null || task.IsCompleted)
        {
            cancellation.Dispose();
            return;
        }

        task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await StreamOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Service-mode core log stream was interrupted: {exception.Message}");
            }

            await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamOnceAsync(CancellationToken cancellationToken)
    {
        await using var stream = await ConnectStreamAsync(_pipeName, cancellationToken).ConfigureAwait(false);
        await WriteHandshakeAsync(stream, cancellationToken).ConfigureAwait(false);
        await ReadHandshakeAsync(stream, cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return;
            }

            if (frame.Value.Opcode == 0x8)
            {
                return;
            }

            if (frame.Value.Opcode == 0x9)
            {
                await WriteClientFrameAsync(stream, 0xA, frame.Value.Payload, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (frame.Value.Opcode is 0x1 or 0x2)
            {
                Publish(Encoding.UTF8.GetString(frame.Value.Payload));
            }
        }
    }

    private static async Task WriteHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var request = string.Join(
            "\r\n",
            "GET /logs?level=debug HTTP/1.1",
            "Host: mihomo",
            "Upgrade: websocket",
            "Connection: Upgrade",
            $"Sec-WebSocket-Key: {key}",
            "Sec-WebSocket-Version: 13",
            "\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(1024);
        var single = new byte[1];
        while (buffer.Count < 8192)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Log WebSocket handshake ended early");
            }

            buffer.Add(single[0]);
            if (buffer.Count >= 4
                && buffer[^4] == '\r'
                && buffer[^3] == '\n'
                && buffer[^2] == '\r'
                && buffer[^1] == '\n')
            {
                var response = Encoding.ASCII.GetString(buffer.ToArray());
                if (!response.StartsWith("HTTP/1.1 101 ", StringComparison.OrdinalIgnoreCase)
                    && !response.StartsWith("HTTP/1.0 101 ", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Log WebSocket handshake failed");
                }

                return;
            }
        }

        throw new IOException("Log WebSocket handshake response is too large");
    }

    private static async Task<WebSocketFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        var headerRead = await ReadExactOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!headerRead)
        {
            return null;
        }

        var opcode = header[0] & 0x0F;
        var isMasked = (header[1] & 0x80) != 0;
        var length = header[1] & 0x7F;
        if (length == 126)
        {
            var extended = new byte[2];
            await ReadExactAsync(stream, extended, cancellationToken).ConfigureAwait(false);
            length = BinaryPrimitives.ReadUInt16BigEndian(extended);
        }
        else if (length == 127)
        {
            var extended = new byte[8];
            await ReadExactAsync(stream, extended, cancellationToken).ConfigureAwait(false);
            var longLength = BinaryPrimitives.ReadUInt64BigEndian(extended);
            if (longLength > MaxFrameBytes)
            {
                throw new IOException("Log WebSocket frame is too large");
            }

            length = (int)longLength;
        }

        if (length > MaxFrameBytes)
        {
            throw new IOException("Log WebSocket frame is too large");
        }

        var mask = Array.Empty<byte>();
        if (isMasked)
        {
            mask = new byte[4];
            await ReadExactAsync(stream, mask, cancellationToken).ConfigureAwait(false);
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if (isMasked)
        {
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] ^= mask[index % 4];
            }
        }

        return new WebSocketFrame(opcode, payload);
    }

    private static async Task WriteClientFrameAsync(Stream stream, int opcode, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > 125)
        {
            payload = payload[..125];
        }

        var mask = RandomNumberGenerator.GetBytes(4);
        var frame = new byte[2 + 4 + payload.Length];
        frame[0] = (byte)(0x80 | opcode);
        frame[1] = (byte)(0x80 | payload.Length);
        mask.CopyTo(frame.AsSpan(2));
        for (var index = 0; index < payload.Length; index++)
        {
            frame[6 + index] = (byte)(payload[index] ^ mask[index % 4]);
        }

        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? false : throw new IOException("Log WebSocket frame ended early");
            }

            offset += read;
        }

        return true;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        if (!await ReadExactOrEndAsync(stream, buffer, cancellationToken).ConfigureAwait(false))
        {
            throw new IOException("Log WebSocket frame ended early");
        }
    }

    private void Publish(string line)
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var message in _parser.Parse(line))
        {
            MessageReceived?.Invoke(this, message);
        }
    }

    private static async Task<Stream> ConnectStreamAsync(string pipeName, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            if (!Path.IsPathRooted(pipeName))
            {
                throw new InvalidOperationException("Core Unix socket path must be absolute.");
            }

            await socket.ConnectAsync(new UnixDomainSocketEndPoint(pipeName), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string NormalizeEndpoint(string pipePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return pipePath;
        }

        const string prefix = @"\\.\pipe\";
        return pipePath.StartsWith(prefix, StringComparison.Ordinal) ? pipePath[prefix.Length..] : pipePath;
    }

    private readonly record struct WebSocketFrame(int Opcode, byte[] Payload);
}
