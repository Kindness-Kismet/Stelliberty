using System.Net;
using System.Net.Sockets;
using System.Text;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Infrastructure.DataManagement;
using Xunit;

namespace Stelliberty.WebDav.Tests;

public sealed class WebDavBackupStoreTests
{
    [Fact(DisplayName = "WebDAV backup store uploads lists and downloads backups")]
    public async Task WebDavBackupStoreUploadsListsAndDownloadsBackups()
    {
        await using var server = TestWebDavServer.Start("test-user", "<webdav-password>");
        using var store = new WebDavBackupStore(new HttpClient(), ownsHttpClient: true);
        var settings = new WebDavBackupSettings(
            server.Url,
            "test-data/backups",
            "test-user",
            "<webdav-password>",
            5);
        var fileName = $"stelliberty-test.{AppRuntimeNames.FileNameToken}";
        var content = Encoding.UTF8.GetBytes("backup-content");

        await store.TestConnectionAsync(settings, CancellationToken.None);
        await store.UploadAsync(settings, fileName, new MemoryStream(content), CancellationToken.None);
        var entries = await store.ListAsync(settings, CancellationToken.None);
        var downloadPath = Path.Combine(Path.GetTempPath(), "stelliberty-webdav-tests", Guid.NewGuid().ToString("N"), fileName);
        await store.DownloadAsync(settings, fileName, downloadPath, CancellationToken.None);
        await store.DeleteAsync(settings, fileName, CancellationToken.None);
        var afterDelete = await store.ListAsync(settings, CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(fileName, entry.FileName);
        Assert.Equal(content.Length, entry.Size);
        Assert.Equal("backup-content", await File.ReadAllTextAsync(downloadPath));
        Assert.Empty(afterDelete);
        Assert.Contains(server.Requests, request => request.Method == "PROPFIND" && request.Path.EndsWith("/test-data/backups/", StringComparison.Ordinal));
        Assert.Contains(server.Requests, request => request.Method == "PUT" && request.Authorization.StartsWith("Basic ", StringComparison.Ordinal));
    }

    private sealed class TestWebDavServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _expectedAuthorization;
        private readonly Dictionary<string, StoredFile> _files = [];
        private readonly HashSet<string> _directories = ["/"];
        private readonly Task _serverTask;
        private int _clock;

        private TestWebDavServer(TcpListener listener, string expectedAuthorization)
        {
            _listener = listener;
            _expectedAuthorization = expectedAuthorization;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{Port}/dav";
            _serverTask = Task.Run(ServeAsync);
        }

        public int Port { get; }

        public string Url { get; }

        public List<RecordedRequest> Requests { get; } = [];

        public static TestWebDavServer Start(string userName, string password)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}"));
            return new TestWebDavServer(listener, $"Basic {token}");
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

            var root = Path.Combine(Path.GetTempPath(), "stelliberty-webdav-tests");
            var fullRoot = Path.GetFullPath(root);
            if (Directory.Exists(fullRoot) && fullRoot.Contains("stelliberty-webdav-tests", StringComparison.Ordinal))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }

        private async Task ServeAsync()
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                Requests.Add(new RecordedRequest(request.Method, request.Path, request.Authorization));
                await HandleAsync(stream, request);
            }
        }

        private async Task HandleAsync(NetworkStream stream, Request request)
        {
            if (request.Authorization != _expectedAuthorization)
            {
                await WriteAsync(stream, "401 Unauthorized", "text/plain", "unauthorized");
                return;
            }

            switch (request.Method)
            {
                case "MKCOL":
                    _directories.Add(DirectoryKey(request.Path));
                    await WriteAsync(stream, "201 Created", "text/plain", "");
                    break;
                case "PUT":
                    _files[FileKey(request.Path)] = new StoredFile(request.Body, DateTimeOffset.UnixEpoch.AddSeconds(++_clock));
                    await WriteAsync(stream, "201 Created", "text/plain", "");
                    break;
                case "GET" when _files.TryGetValue(FileKey(request.Path), out var file):
                    await WriteAsync(stream, "200 OK", "application/octet-stream", file.Content);
                    break;
                case "DELETE":
                    _files.Remove(FileKey(request.Path));
                    await WriteAsync(stream, "204 No Content", "text/plain", "");
                    break;
                case "PROPFIND":
                    await WriteAsync(stream, "207 Multi-Status", "application/xml", BuildPropFindResponse(DirectoryKey(request.Path)));
                    break;
                default:
                    await WriteAsync(stream, "404 Not Found", "text/plain", "not found");
                    break;
            }
        }

        private string BuildPropFindResponse(string directory)
        {
            var builder = new StringBuilder();
            builder.Append("""<?xml version="1.0" encoding="utf-8"?><d:multistatus xmlns:d="DAV:">""");
            builder.Append(DirectoryResponse(directory));
            foreach (var pair in _files.Where(pair => ParentDirectory(pair.Key) == directory).OrderBy(pair => pair.Key))
            {
                builder.Append(FileResponse(pair.Key, pair.Value));
            }

            builder.Append("</d:multistatus>");
            return builder.ToString();
        }

        private static string DirectoryResponse(string directory)
        {
            return $"""
                   <d:response><d:href>{directory}</d:href><d:propstat><d:prop><d:resourcetype><d:collection /></d:resourcetype></d:prop></d:propstat></d:response>
                   """;
        }

        private static string FileResponse(string path, StoredFile file)
        {
            return $"""
                   <d:response><d:href>{path}</d:href><d:propstat><d:prop><d:resourcetype /><d:getcontentlength>{file.Content.Length}</d:getcontentlength><d:getlastmodified>{file.LastModified:R}</d:getlastmodified></d:prop></d:propstat></d:response>
                   """;
        }

        private static async Task<Request> ReadRequestAsync(NetworkStream stream)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1];
            while (!Encoding.ASCII.GetString(bytes.ToArray()).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                bytes.Add(buffer[0]);
            }

            var headerText = Encoding.ASCII.GetString(bytes.ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            var headers = lines
                .Skip(1)
                .Where(line => line.Contains(':'))
                .Select(line => line.Split(':', 2))
                .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var length = headers.TryGetValue("Content-Length", out var rawLength) && int.TryParse(rawLength, out var parsedLength)
                ? parsedLength
                : 0;
            var body = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset, length - offset));
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            return new Request(
                requestLine[0],
                Uri.UnescapeDataString(requestLine[1]),
                headers.GetValueOrDefault("Authorization", string.Empty),
                body);
        }

        private static async Task WriteAsync(NetworkStream stream, string status, string contentType, string text)
        {
            await WriteAsync(stream, status, contentType, Encoding.UTF8.GetBytes(text));
        }

        private static async Task WriteAsync(NetworkStream stream, string status, string contentType, byte[] body)
        {
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers);
            await stream.WriteAsync(body);
        }

        private static string DirectoryKey(string path)
        {
            return path.TrimEnd('/') + "/";
        }

        private static string FileKey(string path)
        {
            return path.TrimEnd('/');
        }

        private static string ParentDirectory(string path)
        {
            var index = path.LastIndexOf('/');
            return index <= 0 ? "/" : path[..index] + "/";
        }

        private sealed record Request(string Method, string Path, string Authorization, byte[] Body);

        private sealed record StoredFile(byte[] Content, DateTimeOffset LastModified);
    }

    public sealed record RecordedRequest(string Method, string Path, string Authorization);
}
