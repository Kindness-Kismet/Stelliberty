using System.Net;
using System.Net.Sockets;
using System.Text;
using Stelliberty.Domain.Overrides;
using Stelliberty.Infrastructure.Overrides;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class HttpRemoteOverrideDownloaderTests
{
    [Fact(DisplayName = "Remote override downloader accepts utf8 charset alias")]
    public async Task RemoteOverrideDownloaderAcceptsUtf8CharsetAlias()
    {
        const string body = "mixed-port: 7890\n# utf8 alias\n";
        await using var server = TestHttpServer.Start(body, "text/plain; charset=utf8");
        var downloader = new HttpRemoteOverrideDownloader();

        var content = await downloader.DownloadAsync(RemoteOverride(server.Url, OverrideUpdateProxyMode.Direct));

        Assert.Equal(body, content);
    }

    [Fact(DisplayName = "Remote override downloader reads current core proxy endpoint")]
    public async Task RemoteOverrideDownloaderReadsCurrentCoreProxyEndpoint()
    {
        const string body = "mixed-port: 7890\n";
        var coreProxyPort = 1;
        await using var proxy = TestHttpServer.Start(body, "text/plain; charset=utf-8");
        var downloader = new HttpRemoteOverrideDownloader(() => ("127.0.0.1", coreProxyPort));
        coreProxyPort = proxy.Port;

        var content = await downloader.DownloadAsync(RemoteOverride(
            "http://override.example/config.yaml",
            OverrideUpdateProxyMode.Core));

        Assert.Equal(body, content);
    }

    private static OverrideProfile RemoteOverride(string sourceLocation, OverrideUpdateProxyMode updateProxyMode)
    {
        return new OverrideProfile(
            "override-1",
            "Remote Override",
            OverrideSourceType.Remote,
            OverrideFormat.Yaml,
            sourceLocation,
            DateTimeOffset.UnixEpoch,
            UpdateProxyMode: updateProxyMode);
    }

    private sealed class TestHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _bodyBytes;
        private readonly string _contentType;
        private readonly Task _serverTask;

        private TestHttpServer(TcpListener listener, byte[] bodyBytes, string contentType)
        {
            _listener = listener;
            _bodyBytes = bodyBytes;
            _contentType = contentType;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{Port}/override";
            _serverTask = Task.Run(ServeAsync);
        }

        public int Port { get; }

        public string Url { get; }

        public static TestHttpServer Start(string body, string contentType)
        {
            return Start(Encoding.UTF8.GetBytes(body), contentType);
        }

        public static TestHttpServer Start(byte[] bodyBytes, string contentType)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new TestHttpServer(listener, bodyBytes, contentType);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or TimeoutException)
            {
            }
        }

        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            await ReadRequestHeadersAsync(stream);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: {_contentType}\r\nContent-Length: {_bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers);
            await stream.WriteAsync(_bodyBytes);
        }

        private static async Task ReadRequestHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[1024];
            var builder = new StringBuilder();
            while (!builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    return;
                }

                builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }
    }
}
