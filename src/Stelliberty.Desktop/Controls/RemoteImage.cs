using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Controls;

// 只负责把 URL 对应的图贴到自身；下载与缓存归 RemoteImageCache。
// 控件重建仅取消"贴图到本控件"，不打断下载，故缓存必能填充、后续同步命中。
public sealed class RemoteImage : Image
{
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(SourceUrl));

    private string? _loadingUrl;

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    static RemoteImage()
    {
        SourceUrlProperty.Changed.AddClassHandler<RemoteImage>((image, _) => image.OnSourceUrlChanged());
    }

    private void OnSourceUrlChanged()
    {
        var url = SourceUrl;
        _loadingUrl = url;

        if (string.IsNullOrWhiteSpace(url))
        {
            Apply(null);
            return;
        }

        // 命中内存缓存同步贴图，不经过空状态，避免闪回退图标。
        if (RemoteImageCache.TryGetMemory(url, out var cached))
        {
            Apply(cached);
            return;
        }

        Apply(null);
        _ = LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        var bitmap = await RemoteImageCache.GetAsync(url);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // URL 已被后续变更取代则丢弃结果，避免贴错图。
            if (_loadingUrl == url && bitmap is not null)
            {
                Apply(bitmap);
            }
        });
    }

    private void Apply(Bitmap? bitmap)
    {
        Source = bitmap;
    }
}
