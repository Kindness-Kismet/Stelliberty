using System.IO.Pipes;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class PipeCoreProxyClient : IProxyCoreClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _client;
    private readonly CancellationTokenSource _streamCancellation = new();
    private int _isStreamStarted;

    // /memory 常从 0 开始；保留最后一个非零值以稳定界面。
    private long _lastMemoryInuse;
    private long _trafficUpload;
    private long _trafficDownload;
    // 0 表示 /traffic 订阅还没拿到数据。
    private long _trafficStampMs;

    public PipeCoreProxyClient(HttpClient client)
    {
        _client = client;
    }

    public PipeCoreProxyClient(string corePipe)
        : this(CreatePipeHttpClient(corePipe))
    {
    }

    public void Dispose()
    {
        // 只取消不释放：订阅任务可能仍在 token 上等待。
        _streamCancellation.Cancel();
        _client.Dispose();
    }

    public static HttpClient CreatePipeHttpClient(string corePipe)
    {
        var pipeName = NormalizeEndpoint(corePipe);
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            // HttpClient 负责 HTTP 语义；本地 IPC 负责传输。
            ConnectCallback = async (_, cancellationToken) =>
            {
                return await ConnectStreamAsync(pipeName, cancellationToken);
            },
        };

        // BaseAddress 只满足 URI 形状；ConnectCallback 会忽略 host。
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
    }

    private static async Task<Stream> ConnectStreamAsync(string pipeName, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken);
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            if (!Path.IsPathRooted(pipeName))
            {
                throw new InvalidOperationException("Core Unix socket path must be absolute.");
            }

            await socket.ConnectAsync(new UnixDomainSocketEndPoint(pipeName), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static string NormalizeEndpoint(string pipePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return pipePath;
        }

        const string prefix = @"\\.\pipe\";
        return pipePath.StartsWith(prefix, StringComparison.Ordinal) ? pipePath[prefix.Length..] : pipePath;
    }

    public async Task<IReadOnlyList<ConnectionInfo>?> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        // 短超时防止核心变慢时每秒轮询长期堆积在途请求
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await _client.GetAsync("connections", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning($"Core connection list read failed: HTTP {(int)response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            return new ConnectionParser().Parse(content, DateTimeOffset.Now);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Core connection list read failed: {exception.Message}");
            return null;
        }
    }

    public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
    {
        var path = $"proxies/{Uri.EscapeDataString(request.GroupName)}";
        var content = new StringContent(JsonSerializer.Serialize(new ChangeProxyPayload(request.ProxyName), JsonOptions));
        return SendAsync(new HttpRequestMessage(HttpMethod.Put, path) { Content = content }, $"switch proxy group={request.GroupName} proxy={request.ProxyName}", cancellationToken);
    }

    public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
    {
        var path = $"proxies/{Uri.EscapeDataString(groupName)}";
        return SendAsync(new HttpRequestMessage(HttpMethod.Delete, path), $"clear pinned proxy selection group={groupName}", cancellationToken);
    }

    public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
    {
        var path = request.Mode == ConnectionCloseMode.Single && !string.IsNullOrWhiteSpace(request.ConnectionId)
            ? $"connections/{Uri.EscapeDataString(request.ConnectionId)}"
            : "connections";
        return SendAsync(new HttpRequestMessage(HttpMethod.Delete, path), "close connection", cancellationToken);
    }

    public async Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var proxiesResponse = await _client.GetAsync("proxies", cancellationToken);
            proxiesResponse.EnsureSuccessStatusCode();
            var proxiesJson = await proxiesResponse.Content.ReadAsStringAsync(cancellationToken);
            using var proxiesDocument = JsonDocument.Parse(proxiesJson);
            var entriesByName = ParseProxyEntries(proxiesDocument.RootElement);

            using var providersResponse = await _client.GetAsync("providers/proxies", cancellationToken);
            providersResponse.EnsureSuccessStatusCode();
            var providersJson = await providersResponse.Content.ReadAsStringAsync(cancellationToken);
            using var providersDocument = JsonDocument.Parse(providersJson);
            MergeProviderEntries(entriesByName, providersDocument.RootElement);
            var entries = entriesByName.Values.ToList();

            // /proxies 键顺序不是订阅顺序；GLOBAL.all 才是。
            var global = entries.FirstOrDefault(entry => string.Equals(entry.Name, "GLOBAL", StringComparison.Ordinal));
            if (global is { All.Count: > 0 })
            {
                var order = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var index = 0; index < global.All.Count; index++)
                {
                    order[global.All[index]] = index;
                }

                entries = entries
                    .OrderBy(entry => order.TryGetValue(entry.Name, out var rank) ? rank : int.MaxValue)
                    .ToList();
            }

            return new ProxyRuntimeSnapshot(entries);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Core proxy list read failed: {ex.Message}");
            return new ProxyRuntimeSnapshot([]);
        }
    }

    private static Dictionary<string, ProxyRuntimeEntry> ParseProxyEntries(JsonElement root)
    {
        var entries = new Dictionary<string, ProxyRuntimeEntry>(StringComparer.Ordinal);
        if (!root.TryGetProperty("proxies", out var proxies) || proxies.ValueKind != JsonValueKind.Object)
        {
            return entries;
        }

        foreach (var proxy in proxies.EnumerateObject())
        {
            entries[proxy.Name] = ParseProxyEntry(proxy.Name, proxy.Value);
        }

        return entries;
    }

    private static void MergeProviderEntries(
        Dictionary<string, ProxyRuntimeEntry> entries,
        JsonElement root)
    {
        if (!root.TryGetProperty("providers", out var providers) || providers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var provider in providers.EnumerateObject())
        {
            if (!provider.Value.TryGetProperty("proxies", out var proxies) || proxies.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var proxy in proxies.EnumerateArray())
            {
                if (proxy.ValueKind != JsonValueKind.Object
                    || !proxy.TryGetProperty("name", out var nameNode)
                    || nameNode.ValueKind != JsonValueKind.String
                    || string.IsNullOrEmpty(nameNode.GetString()))
                {
                    continue;
                }

                var name = nameNode.GetString()!;
                entries.TryAdd(name, ParseProxyEntry(name, proxy, provider.Name));
            }
        }
    }

    private static ProxyRuntimeEntry ParseProxyEntry(string name, JsonElement node, string? providerName = null)
    {
        var type = node.TryGetProperty("type", out var typeNode) ? typeNode.GetString() ?? string.Empty : string.Empty;
        var now = node.TryGetProperty("now", out var nowNode) && nowNode.ValueKind == JsonValueKind.String
            ? nowNode.GetString()
            : null;
        var fixedSelection = node.TryGetProperty("fixed", out var fixedNode) && fixedNode.ValueKind == JsonValueKind.String
            ? fixedNode.GetString()
            : null;
        var all = node.TryGetProperty("all", out var allNode) && allNode.ValueKind == JsonValueKind.Array
            ? allNode.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList()
            : [];
        var isHidden = node.TryGetProperty("hidden", out var hiddenNode) && hiddenNode.ValueKind == JsonValueKind.True;
        var dialerProxy = node.TryGetProperty("dialer-proxy", out var dialerProxyNode)
            && dialerProxyNode.ValueKind == JsonValueKind.String
                ? dialerProxyNode.GetString()
                : null;
        return new ProxyRuntimeEntry(name, type, now, fixedSelection, all, isHidden, providerName, dialerProxy);
    }

    public async Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetAsync("configs", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning($"Core runtime config read failed: HTTP {(int)response.StatusCode}");
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("mode", out var modeNode) && modeNode.ValueKind == JsonValueKind.String)
            {
                return OutboundModeParser.TryParse(modeNode.GetString());
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Core runtime config read failed: {ex.Message}");
            return null;
        }
    }

    public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default)
    {
        var modeText = mode switch
        {
            OutboundMode.Global => "global",
            OutboundMode.Direct => "direct",
            _ => "rule",
        };
        var content = new StringContent(JsonSerializer.Serialize(new ModePayload(modeText), JsonOptions));
        return SendAsync(new HttpRequestMessage(HttpMethod.Patch, "configs") { Content = content }, "switch outbound mode", cancellationToken);
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetAsync("version", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Core version read failed: {ex.Message}");
            return null;
        }
    }

    public async Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
    {
        EnsureStreamSubscriptions();
        try
        {
            long uploadTotal;
            long downloadTotal;
            int connectionCount;
            using (var response = await _client.GetAsync("connections", cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                uploadTotal = ReadLong(root, "uploadTotal");
                downloadTotal = ReadLong(root, "downloadTotal");
                connectionCount = root.TryGetProperty("connections", out var connections) && connections.ValueKind == JsonValueKind.Array
                    ? connections.GetArrayLength()
                    : 0;
            }

            var traffic = ReadCachedTraffic();
            return new CoreRuntimeStats(
                traffic?.UploadSpeed ?? 0,
                traffic?.DownloadSpeed ?? 0,
                uploadTotal,
                downloadTotal,
                connectionCount,
                Interlocked.Read(ref _lastMemoryInuse),
                traffic is not null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            AppLogger.Warning($"Core runtime stats read failed: {ex.Message}");
            return null;
        }
    }

    // 速率取订阅缓存：/traffic 每秒推一行，逐次新建连接会平白等满一个推送周期。
    public Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
    {
        EnsureStreamSubscriptions();
        return Task.FromResult(ReadCachedTraffic());
    }

    private CoreTrafficRate? ReadCachedTraffic()
    {
        var stamp = Interlocked.Read(ref _trafficStampMs);
        // 超过两个推送周期没有新行即视为失效，交由调用方改用总量差值。
        if (stamp == 0 || Environment.TickCount64 - stamp > 2500)
        {
            return null;
        }

        return new CoreTrafficRate(
            Interlocked.Read(ref _trafficUpload),
            Interlocked.Read(ref _trafficDownload));
    }

    private void EnsureStreamSubscriptions()
    {
        if (Interlocked.CompareExchange(ref _isStreamStarted, 1, 0) != 0)
        {
            return;
        }

        var cancellationToken = _streamCancellation.Token;
        _ = Task.Run(() => SubscribeStreamAsync("traffic", ApplyTrafficLine, cancellationToken), cancellationToken);
        _ = Task.Run(() => SubscribeStreamAsync("memory", ApplyMemoryLine, cancellationToken), cancellationToken);
    }

    // 核心停止或重启会断开流；退避重连，不能忙等。
    private async Task SubscribeStreamAsync(string path, Action<JsonElement> apply, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            apply(doc.RootElement);
                        }
                        catch (JsonException)
                        {
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
            {
                AppLogger.Debug($"Core {path} stream ended: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ApplyTrafficLine(JsonElement root)
    {
        if (TryReadLong(root, "up", out var uploadSpeed) && TryReadLong(root, "down", out var downloadSpeed))
        {
            Interlocked.Exchange(ref _trafficUpload, uploadSpeed);
            Interlocked.Exchange(ref _trafficDownload, downloadSpeed);
            Interlocked.Exchange(ref _trafficStampMs, Environment.TickCount64);
        }
    }

    private void ApplyMemoryLine(JsonElement root)
    {
        if (TryReadLong(root, "inuse", out var inuse) && inuse > 0)
        {
            Interlocked.Exchange(ref _lastMemoryInuse, inuse);
        }
    }

    private static long ReadLong(JsonElement element, string name)
        => TryReadLong(element, name, out var value) ? value : 0;

    private static bool TryReadLong(JsonElement element, string name, out long value)
    {
        if (element.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt64(out var parsed))
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    private async Task<bool> SendAsync(HttpRequestMessage request, string operationName, CancellationToken cancellationToken)
    {
        try
        {
            using (request)
            {
                using var response = await _client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    AppLogger.Info($"Core operation succeeded: {operationName}");
                    return true;
                }

                AppLogger.Warning($"Core operation failed: {operationName} HTTP {(int)response.StatusCode}");
                return false;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLogger.Warning($"Core operation failed: {operationName} {exception.Message}");
            return false;
        }
    }

    private sealed record ChangeProxyPayload([property: JsonPropertyName("name")] string Name);

    private sealed record ModePayload([property: JsonPropertyName("mode")] string Mode);
}
