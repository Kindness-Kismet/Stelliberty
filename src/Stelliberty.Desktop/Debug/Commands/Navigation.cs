#if DEBUG
using System.Globalization;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AppNavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static string? ExecuteNavigationCommand(MainWindow window, string command)
    {
        if (command.StartsWith("page.scroll_y", StringComparison.OrdinalIgnoreCase))
        {
            return ReadOrSetCurrentPageScrollViewerY(window, command["page.scroll_y".Length..].Trim()).ToString("0.###", CultureInfo.InvariantCulture);
        }

        if (command.StartsWith("page.open ", StringComparison.OrdinalIgnoreCase))
        {
            Navigate(window, command["page.open ".Length..].Trim());
            return null;
        }

        throw new InvalidOperationException($"Unknown page command: {command}");
    }

    private static void Navigate(MainWindow window, string spec)
    {
        if (window.DataContext is not MainWindowViewModel viewModel)
        {
            throw new InvalidOperationException("DataContext is not ready");
        }

        var key = spec.ToLowerInvariant();
        switch (key)
        {
            case "home": viewModel.CurrentPage = AppNavigationPage.Home; break;
            case "proxies": viewModel.CurrentPage = AppNavigationPage.Proxy; break;
            case "connections": viewModel.CurrentPage = AppNavigationPage.Connections; break;
            case "core-logs": viewModel.CurrentPage = AppNavigationPage.CoreLogs; break;
            case "rules": viewModel.CurrentPage = AppNavigationPage.Rules; break;
            case "subscriptions": viewModel.CurrentPage = AppNavigationPage.Subscriptions; break;
            case "overrides": viewModel.CurrentPage = AppNavigationPage.Overrides; break;
            case "settings":
            case "settings/root":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Root;
                break;
            case "settings/theme":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Theme;
                break;
            case "settings/language":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Language;
                break;
            case "settings/clash-features":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.ClashFeatures;
                break;
            case "settings/app-behavior":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.AppBehavior;
                break;
            case "settings/data-management":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.DataManagement;
                break;
            case "settings/update":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Update;
                break;
            case "settings/about":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.About;
                break;
            case "settings/app-log":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.AppLog;
                break;
            case "settings/network":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Network;
                break;
            case "settings/port-control":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.PortControl;
                break;
            case "settings/system-integration":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.SystemIntegration;
                break;
            case "settings/dns":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Dns;
                break;
            case "settings/performance":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.Performance;
                break;
            case "settings/core-log":
                viewModel.CurrentPage = AppNavigationPage.Settings;
                viewModel.Settings.SubPage = SettingsSubPage.CoreLog;
                break;
            default:
                throw new InvalidOperationException($"Unknown page: {spec}");
        }
    }

    private static double ReadOrSetCurrentPageScrollViewerY(MainWindow window, string spec)
    {
        var text = spec.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return FindCurrentPageScrollViewer(window).Offset.Y;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            throw new InvalidOperationException("page.scroll_y usage: page.scroll_y [y]");
        }

        var scrollViewer = FindCurrentPageScrollViewer(window);
        scrollViewer.Offset = scrollViewer.Offset.WithY(y);
        return scrollViewer.Offset.Y;
    }

    private static ScrollViewer FindCurrentPageScrollViewer(MainWindow window)
    {
        return window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Where(IsControlEffectivelyVisible)
            .OrderByDescending(scrollViewer => scrollViewer.Bounds.Height)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Current page has no visible scroll container");
    }
}
#endif
