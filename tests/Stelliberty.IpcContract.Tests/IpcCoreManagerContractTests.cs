using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Stelliberty.Application.Runtime;
using Stelliberty.Infrastructure.Core;
using Xunit;

namespace Stelliberty.IpcContract.Tests;

public sealed class IpcCoreManagerContractTests
{
    [Fact(DisplayName = "GetSnapshotAsync uses the status method and parses the snapshot contract")]
    public async Task GetSnapshotAsyncUsesStatusMethodAndParsesSnapshotContract()
    {
        await using var server = new FakeJsonRpcPipeServer(request =>
        {
            return Method(request) == "core.status"
                ? Success(request, new { state = "running", pid = 321, external_controller = "pipe://mihomo", last_error = (string?)null })
                : Error(request, "contract.unexpected_method", Method(request));
        });
        await using var manager = new IpcCoreManager(server.PipeName);

        var snapshot = await manager.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(CoreState.Running, snapshot.State);
        Assert.Equal(321, snapshot.Pid);
        Assert.Equal("pipe://mihomo", snapshot.ExternalController);
        Assert.Null(snapshot.LastError);
        var request = Assert.Single(server.Requests);
        Assert.Equal("core.status", Method(request));
        Assert.Empty(request.GetProperty("params").EnumerateObject());
    }

    [Fact(DisplayName = "ApplyConfigAsync uses snake_case params and parses the apply-result contract")]
    public async Task ApplyConfigAsyncUsesSnakeCaseParamsAndParsesApplyResultContract()
    {
        await using var server = new FakeJsonRpcPipeServer(request =>
        {
            return Method(request) == "core.apply_config"
                ? Success(request, new { mode = "reload", pid = 654 })
                : Error(request, "contract.unexpected_method", Method(request));
        });
        await using var manager = new IpcCoreManager(server.PipeName);

        var result = await manager.ApplyConfigAsync(
            new CoreApplyConfigRequest("test-data/runtime/selected.yaml", "sub-1"),
            CancellationToken.None);

        Assert.Equal(CoreApplyMode.Reload, result.Mode);
        Assert.Equal(654, result.Pid);
        var request = Assert.Single(server.Requests);
        var parameters = request.GetProperty("params");
        Assert.Equal("core.apply_config", Method(request));
        Assert.Equal("test-data/runtime/selected.yaml", parameters.GetProperty("runtime_yaml_path").GetString());
        Assert.Equal("sub-1", parameters.GetProperty("subscription_id").GetString());
    }

    [Fact(DisplayName = "ApplyConfigAsync propagates the remote error-code contract")]
    public async Task ApplyConfigAsyncPropagatesRemoteErrorCodeContract()
    {
        await using var server = new FakeJsonRpcPipeServer(request =>
        {
            return Method(request) == "core.apply_config"
                ? Error(request, "core.yaml_invalid", "bad yaml")
                : Error(request, "contract.unexpected_method", Method(request));
        });
        await using var manager = new IpcCoreManager(server.PipeName);

        var exception = await Assert.ThrowsAsync<IpcRemoteException>(() => manager.ApplyConfigAsync(
            new CoreApplyConfigRequest("bad.yaml", "sub-1"),
            CancellationToken.None));

        Assert.Equal("core.yaml_invalid", exception.Code);
        Assert.Equal("bad yaml", exception.Message);
    }

    [Fact(DisplayName = "RestartAsync uses the restart method and waits for a running snapshot")]
    public async Task RestartAsyncUsesRestartMethodAndWaitsForRunningSnapshotContract()
    {
        await using var server = new FakeJsonRpcPipeServer(request =>
        {
            return Method(request) switch
            {
                "core.restart" => Success(request, new { pid = 987 }),
                "core.status" => Success(request, new { state = "running", pid = 987, external_controller = "pipe://mihomo", last_error = (string?)null }),
                _ => Error(request, "contract.unexpected_method", Method(request)),
            };
        });
        await using var manager = new IpcCoreManager(server.PipeName);

        await manager.RestartAsync(CancellationToken.None);

        Assert.Equal(["core.restart", "core.status"], server.Requests.Select(Method));
    }

    private static string Method(JsonElement request)
    {
        return request.GetProperty("method").GetString() ?? string.Empty;
    }

    private static JsonElement Success(JsonElement request, object result)
    {
        return JsonSerializer.SerializeToElement(new
        {
            id = request.GetProperty("id").GetString(),
            result,
        });
    }

    private static JsonElement Error(JsonElement request, string code, string message)
    {
        return JsonSerializer.SerializeToElement(new
        {
            id = request.GetProperty("id").GetString(),
            error = new
            {
                code,
                message,
            },
        });
    }

    private sealed class FakeJsonRpcPipeServer : IAsyncDisposable
    {
        private readonly Func<JsonElement, JsonElement> _handler;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly ConcurrentQueue<JsonElement> _requests = new();
        private readonly Task _serverTask;
        private readonly NamedPipeServerStream? _pipe;
        private readonly Socket? _listener;
        private readonly string? _socketPath;

        public FakeJsonRpcPipeServer(Func<JsonElement, JsonElement> handler)
        {
            _handler = handler;
            if (OperatingSystem.IsWindows())
            {
                PipeName = $"stelliberty.ipc_contract.{Guid.NewGuid():N}";
                _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }
            else
            {
                _socketPath = ShortUnixSocketPath();
                PipeName = _socketPath;
                if (File.Exists(_socketPath))
                {
                    File.Delete(_socketPath);
                }

                _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
                _listener.Listen(1);
            }

            _serverTask = Task.Run(RunAsync);
        }

        public string PipeName { get; }

        private static string ShortUnixSocketPath()
        {
            // Unix socket 路径长度受平台限制，测试固定用短目录。
            return Path.Combine("/tmp", $"slb-ipc-{Guid.NewGuid():N}.sock");
        }

        public IReadOnlyList<JsonElement> Requests => _requests.Select(request => request.Clone()).ToArray();

        private async Task RunAsync()
        {
            try
            {
                await using var stream = await AcceptStreamAsync(_cancellation.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
                while (!_cancellation.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    using var document = JsonDocument.Parse(line);
                    var request = document.RootElement.Clone();
                    _requests.Enqueue(request);
                    var response = _handler(request);
                    var responseText = JsonSerializer.Serialize(response);
                    await writer.WriteLineAsync(responseText.AsMemory(), _cancellation.Token).ConfigureAwait(false);
                    await writer.FlushAsync(_cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (IOException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
            }
        }

        private async Task<Stream> AcceptStreamAsync(CancellationToken cancellationToken)
        {
            if (_pipe is not null)
            {
                await _pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                return _pipe;
            }

            if (_listener is null)
            {
                throw new InvalidOperationException("IPC test server is not initialized.");
            }

            var socket = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _pipe?.Dispose();
            _listener?.Dispose();
            try
            {
                await _serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (_socketPath is not null && File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            _cancellation.Dispose();
        }
    }
}
