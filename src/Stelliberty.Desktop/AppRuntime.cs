using System.Reflection;
using Avalonia;
using Avalonia.Media;
#if DEBUG
using HotAvalonia;
#endif
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop;

internal static class AppRuntime
{
    private static string AppFontFamily => $"avares://{AppRuntimeNames.UiResourceAuthority}/Assets/fonts#Google Sans";
    private static string CjkFontFamily => $"avares://{AppRuntimeNames.UiResourceAuthority}/Assets/fonts#Noto Sans SC";

    public static void RunUi(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        AppLogger.Info("Desktop UI startup");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppLogger.Info("Desktop UI shutdown");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "App startup failed");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(CreateFontManagerOptions())
            .LogToTrace();

        if (OperatingSystem.IsLinux())
        {
#pragma warning disable AVALONIA_X11_CSD
            builder = builder.With(new X11PlatformOptions
            {
                EnableDrawnDecorations = true,
            });
#pragma warning restore AVALONIA_X11_CSD
        }
#if DEBUG
        builder = builder.UseHotReload(ResolveProjectPath);
#endif
        return builder;
    }

    private static FontManagerOptions CreateFontManagerOptions()
    {
        var emojiRange = UnicodeRange.Parse("U+1F000-1FAFF, U+2600-27BF, U+2B00-2BFF");

        return new FontManagerOptions
        {
            DefaultFamilyName = AppFontFamily,
            FontFallbacks =
            [
                new FontFallback { FontFamily = new FontFamily(CjkFontFamily) },
                new FontFallback { FontFamily = new FontFamily("Segoe UI Emoji"), UnicodeRange = emojiRange },
                new FontFallback { FontFamily = new FontFamily("Apple Color Emoji"), UnicodeRange = emojiRange },
                new FontFallback { FontFamily = new FontFamily("Noto Color Emoji"), UnicodeRange = emojiRange },
            ],
        };
    }

#if DEBUG
    private static string? ResolveProjectPath(Assembly assembly)
    {
        return assembly.GetName().Name == AppRuntimeNames.UiResourceAuthority ? FindDesktopProjectPath() : null;
    }

    private static string? FindDesktopProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectPath = Path.Combine(directory.FullName, "src", "Stelliberty.Desktop", "Stelliberty.Desktop.csproj");
            if (File.Exists(projectPath))
            {
                return Path.GetDirectoryName(projectPath);
            }

            directory = directory.Parent;
        }

        return null;
    }
#endif

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            AppLogger.Error(exception, "Unhandled exception");
            return;
        }

        AppLogger.Error($"Unhandled exception: {args.ExceptionObject}");
    }
}
