using System.Text.Json;
using System.Text.Json.Serialization;
using Stelliberty.Application.Proxies;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class PipeCoreProxyDelayTester : IProxyDelayTester, IDisposable
{
    private readonly HttpClient _client;
    private readonly Func<string> _testUrlFactory;
    private readonly int _timeoutMilliseconds;

    public PipeCoreProxyDelayTester(HttpClient client, string testUrl, int timeoutMilliseconds)
        : this(client, () => testUrl, timeoutMilliseconds)
    {
    }

    public PipeCoreProxyDelayTester(HttpClient client, Func<string> testUrlFactory, int timeoutMilliseconds)
    {
        _client = client;
        _testUrlFactory = testUrlFactory;
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    public PipeCoreProxyDelayTester(string mihomoPipe, string testUrl, int timeoutMilliseconds)
        : this(PipeCoreProxyClient.CreatePipeHttpClient(mihomoPipe), testUrl, timeoutMilliseconds)
    {
    }

    public PipeCoreProxyDelayTester(string mihomoPipe, Func<string> testUrlFactory, int timeoutMilliseconds)
        : this(PipeCoreProxyClient.CreatePipeHttpClient(mihomoPipe), testUrlFactory, timeoutMilliseconds)
    {
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public async Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
    {
        try
        {
            var testUrl = _testUrlFactory();
            var path = $"proxies/{Uri.EscapeDataString(proxyName)}/delay?timeout={_timeoutMilliseconds}&url={Uri.EscapeDataString(testUrl)}";
            using var response = await _client.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return -1;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<DelayPayload>(stream, cancellationToken: cancellationToken);
            return NormalizeDelay(payload?.Delay ?? -1);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return -1;
        }
    }

    private static int NormalizeDelay(int delay)
        => delay <= 0 ? -1 : delay;

    private sealed class DelayPayload
    {
        [JsonPropertyName("delay")]
        public int Delay { get; set; }
    }
}
