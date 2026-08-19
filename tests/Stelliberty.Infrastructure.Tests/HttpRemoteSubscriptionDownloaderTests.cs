using System.Net;
using System.Net.Sockets;
using System.Text;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Subscriptions;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class HttpRemoteSubscriptionDownloaderTests
{
    [Fact(DisplayName = "Remote subscription downloader accepts utf8 charset alias")]
    public async Task RemoteSubscriptionDownloaderAcceptsUtf8CharsetAlias()
    {
        const string body = "proxies: []\nproxy-groups: []\nrules: []\n# utf8 alias\n";
        await using var server = TestHttpServer.Start(body, "text/plain; charset=utf8");
        var downloader = new HttpRemoteSubscriptionDownloader();

        var result = await downloader.DownloadAsync(new RemoteSubscriptionDownloadRequest(
            server.Url,
            string.Empty,
            SubscriptionUpdateProxyMode.Direct));

        Assert.Equal(body, result.Content);
    }

    [Fact(DisplayName = "Remote subscription downloader reads current core proxy endpoint")]
    public async Task RemoteSubscriptionDownloaderReadsCurrentCoreProxyEndpoint()
    {
        const string body = "proxies: []\nproxy-groups: []\nrules: []\n";
        var coreProxyPort = 1;
        await using var proxy = TestHttpServer.Start(body, "text/plain; charset=utf-8");
        var downloader = new HttpRemoteSubscriptionDownloader(() => ("127.0.0.1", coreProxyPort));
        coreProxyPort = proxy.Port;

        var result = await downloader.DownloadAsync(new RemoteSubscriptionDownloadRequest(
            "http://subscription.example/config.yaml",
            string.Empty,
            SubscriptionUpdateProxyMode.Core));

        Assert.Equal(body, result.Content);
    }

    [Fact(DisplayName = "Remote subscription downloader accepts gbk charset")]
    public async Task RemoteSubscriptionDownloaderAcceptsGbkCharset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        const string body = "中文";
        await using var server = TestHttpServer.Start(Encoding.GetEncoding("gbk").GetBytes(body), "text/plain; charset=gbk");
        var downloader = new HttpRemoteSubscriptionDownloader();

        var result = await downloader.DownloadAsync(new RemoteSubscriptionDownloadRequest(
            server.Url,
            string.Empty,
            SubscriptionUpdateProxyMode.Direct));

        Assert.Equal(body, result.Content);
    }

    [Fact(DisplayName = "Remote subscription downloader rejects unknown declared charset")]
    public async Task RemoteSubscriptionDownloaderRejectsUnknownDeclaredCharset()
    {
        await using var server = TestHttpServer.Start("plain text", "text/plain; charset=x-unknown-charset");
        var downloader = new HttpRemoteSubscriptionDownloader();

        await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadAsync(new RemoteSubscriptionDownloadRequest(
            server.Url,
            string.Empty,
            SubscriptionUpdateProxyMode.Direct)));
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
            Url = $"http://127.0.0.1:{Port}/subscription";
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
