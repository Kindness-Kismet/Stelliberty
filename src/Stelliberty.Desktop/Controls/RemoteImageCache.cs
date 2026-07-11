using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace Stelliberty.Desktop.Controls;

// 图标资源的进程级缓存；下载生命周期与控件解耦，控件重建不中断下载。
public static class RemoteImageCache
{
    private const int MaxImageBytes = 512 * 1024;
    private const int MaxMemoryItems = 128;

    private static readonly object MemoryGate = new();
    private static readonly Dictionary<string, Bitmap> Memory = new(StringComparer.Ordinal);
    private static readonly Queue<string> MemoryOrder = new();

    private static readonly object InflightGate = new();
    private static readonly Dictionary<string, Task<Bitmap?>> Inflight = new(StringComparer.Ordinal);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static volatile string? _diskDirectory;

    // 启动时配置磁盘缓存目录；不可用则降级为纯内存缓存。
    public static void Configure(string diskDirectory)
    {
        try
        {
            Directory.CreateDirectory(diskDirectory);
            _diskDirectory = diskDirectory;
        }
        catch
        {
            _diskDirectory = null;
        }
    }

    public static bool TryGetMemory(string url, out Bitmap bitmap)
    {
        lock (MemoryGate)
        {
            return Memory.TryGetValue(url, out bitmap!);
        }
    }

    // 同 URL 并发合并为一个任务
    public static Task<Bitmap?> GetAsync(string url)
    {
        if (TryGetMemory(url, out var cached))
        {
            return Task.FromResult<Bitmap?>(cached);
        }

        lock (InflightGate)
        {
            if (Inflight.TryGetValue(url, out var existing))
            {
                return existing;
            }

            var task = LoadAsync(url);
            Inflight[url] = task;
            return task;
        }
    }

    private static async Task<Bitmap?> LoadAsync(string url)
    {
        try
        {
            var bytes = ReadDisk(url) ?? await DownloadAsync(url);
            if (bytes is null)
            {
                return null;
            }

            var bitmap = DecodeBitmap(bytes);
            if (bitmap is null)
            {
                return null;
            }

            AddToMemory(url, bitmap);
            return bitmap;
        }
        catch
        {
            return null; // 图标失败不阻断代理组展示
        }
        finally
        {
            lock (InflightGate)
            {
                Inflight.Remove(url);
            }
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        if (!TryCreateHttpUri(url, out var uri))
        {
            return null;
        }

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes)
        {
            return null;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await responseStream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > MaxImageBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }

        var bytes = memory.ToArray();
        WriteDisk(url, bytes);
        return bytes;
    }

    private static Bitmap? DecodeBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null; // 损坏或非法图像当作 miss
        }
    }

    private static byte[]? ReadDisk(string url)
    {
        var path = DiskPath(url);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length <= MaxImageBytes ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteDisk(string url, byte[] bytes)
    {
        var path = DiskPath(url);
        if (path is null)
        {
            return;
        }

        try
        {
            // 临时文件 + 原子替换，避免并发写出半截文件被读到。
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // 磁盘缓存失败仅损失持久化，不影响本次显示。
        }
    }

    private static string? DiskPath(string url)
    {
        var directory = _diskDirectory;
        if (directory is null)
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(directory, hash);
    }

    private static void AddToMemory(string url, Bitmap bitmap)
    {
        lock (MemoryGate)
        {
            if (!Memory.ContainsKey(url))
            {
                MemoryOrder.Enqueue(url);
            }

            Memory[url] = bitmap;
            while (Memory.Count > MaxMemoryItems && MemoryOrder.TryDequeue(out var removed))
            {
                Memory.Remove(removed);
            }
        }
    }

    private static bool TryCreateHttpUri(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        uri = null!;
        return false;
    }
}
