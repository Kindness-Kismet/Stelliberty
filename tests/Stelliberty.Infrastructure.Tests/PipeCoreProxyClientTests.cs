using System.Net;
using System.Text;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Infrastructure.Proxies;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class PipeCoreProxyClientTests
{
    [Fact(DisplayName = "Core proxy client merges separated provider nodes from mihomo 1.19.28")]
    public async Task CoreProxyClientMergesSeparatedProviderNodes()
    {
        using var client = CreateClient(
            """
            {
              "proxies": {
                "GLOBAL": { "type": "Selector", "now": "Select", "all": ["DIRECT", "Provider-US", "Select"] },
                "Select": { "type": "Selector", "now": "Provider-US", "all": ["DIRECT", "Provider-US"] },
                "DIRECT": { "type": "Direct" }
              }
            }
            """,
            """
            {
              "providers": {
                "DemoProvider": {
                  "name": "DemoProvider",
                  "type": "Proxy",
                  "vehicleType": "HTTP",
                  "proxies": [
                    { "name": "Provider-US", "type": "Vless" }
                  ]
                }
              }
            }
            """);

        var snapshot = await client.GetProxiesAsync();

        Assert.Equal(["DIRECT", "Provider-US", "Select", "GLOBAL"], snapshot.Entries.Select(entry => entry.Name));
        var providerNode = snapshot.Entries.Single(entry => entry.Name == "Provider-US");
        Assert.Equal("Vless", providerNode.Type);
        Assert.Equal("DemoProvider", providerNode.ProviderName);
    }

    [Fact(DisplayName = "Core delay tester uses provider healthcheck endpoint for provider nodes")]
    public async Task CoreDelayTesterUsesProviderHealthcheckEndpoint()
    {
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            return request.RequestUri?.AbsolutePath switch
            {
                "/providers/proxies/Demo%20Provider/Provider%2FUS/healthcheck" => JsonResponse("""{ "delay": 12 }"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        using var tester = new PipeCoreProxyDelayTester(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            "http://delay.test/generate_204",
            5000);

        var delay = await tester.TestProviderDelayAsync("Demo Provider", "Provider/US");

        Assert.Equal(12, delay);
        Assert.Equal(
            "/providers/proxies/Demo%20Provider/Provider%2FUS/healthcheck?timeout=5000&url=http%3A%2F%2Fdelay.test%2Fgenerate_204",
            Assert.Single(requestedPaths));
    }

    [Fact(DisplayName = "Core delay tester keeps direct proxy endpoint for non-provider nodes")]
    public async Task CoreDelayTesterKeepsDirectProxyEndpoint()
    {
        var requestedPaths = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            return request.RequestUri?.AbsolutePath switch
            {
                "/proxies/DIRECT/delay" => JsonResponse("""{ "delay": 8 }"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        using var tester = new PipeCoreProxyDelayTester(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            "http://delay.test/generate_204",
            5000);

        var delay = await tester.TestDelayAsync("DIRECT");

        Assert.Equal(8, delay);
        Assert.StartsWith("/proxies/DIRECT/delay?", Assert.Single(requestedPaths), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Proxy delay service routes provider node using runtime source metadata")]
    public async Task ProxyDelayServiceRoutesProviderNodeUsingRuntimeSourceMetadata()
    {
        var tester = new RecordingProviderDelayTester();
        var service = new ProxyDelayService(tester);
        var config = new ProxyConfig(
            [new ProxyGroup("Select", ProxyGroupTypes.Select, "Provider-US", ["Provider-US"])],
            new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
            {
                ["Provider-US"] = new ProxyNode("Provider-US", "Vless", ProviderName: "DemoProvider"),
            });

        var result = await service.TestNodeAsync(config, "Provider-US");

        Assert.Equal(("DemoProvider", "Provider-US"), tester.ProviderRequest);
        Assert.Empty(tester.DirectRequests);
        Assert.Equal(12, result.Config.Nodes["Provider-US"].Delay);
    }

    [Fact(DisplayName = "Core proxy client propagates caller cancellation while loading providers")]
    public async Task CoreProxyClientPropagatesCallerCancellation()
    {
        using var client = new PipeCoreProxyClient(new HttpClient(new BlockingHttpMessageHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetProxiesAsync(cancellation.Token));
    }

    [Fact(DisplayName = "Core delay tester propagates caller cancellation during provider healthcheck")]
    public async Task CoreDelayTesterPropagatesCallerCancellation()
    {
        using var tester = new PipeCoreProxyDelayTester(
            new HttpClient(new BlockingHttpMessageHandler()) { BaseAddress = new Uri("http://localhost/") },
            "http://delay.test/generate_204",
            5000);
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tester.TestProviderDelayAsync("DemoProvider", "Provider-US", cancellation.Token));
    }

    private static PipeCoreProxyClient CreateClient(string proxiesJson, string providersJson)
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/proxies" => JsonResponse(proxiesJson),
            "/providers/proxies" => JsonResponse(providersJson),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        return new PipeCoreProxyClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class RecordingProviderDelayTester : IProviderProxyDelayTester
    {
        public List<string> DirectRequests { get; } = [];

        public (string ProviderName, string ProxyName)? ProviderRequest { get; private set; }

        public Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
        {
            DirectRequests.Add(proxyName);
            return Task.FromResult(8);
        }

        public Task<int> TestProviderDelayAsync(
            string providerName,
            string proxyName,
            CancellationToken cancellationToken = default)
        {
            ProviderRequest = (providerName, proxyName);
            return Task.FromResult(12);
        }
    }
}
