using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Stelliberty.Desktop.Controls;
using Stelliberty.Presentation.ViewModels;
using NavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;

namespace Stelliberty.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private SettingsPageViewModel? _settings;
    private readonly PagePointeroverSuppressor _pointeroverSuppressor;
    private long _subPageAnimationVersion;

    public SettingsView()
    {
        InitializeComponent();
        _pointeroverSuppressor = new PagePointeroverSuppressor(SettingsContentPanel, "settings-row");
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        _settings = _viewModel?.Settings;
        if (_viewModel is not null)
        {
            _settings!.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    // 子页路由切换时内容区淡入上浮；仅在已处于设置页时播放，
    // 避免与进入设置页的主导航过渡叠加成双重动画。
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsPageViewModel.SubPage)
            && _viewModel?.CurrentPage == NavigationPage.Settings)
        {
            AnimateSubPageEnter();
        }
    }

    private void AnimateSubPageEnter()
    {
        var version = ++_subPageAnimationVersion;

        // 切页会按旧鼠标坐标重算命中，首帧禁止继承 hover。
        _pointeroverSuppressor.Begin();

        // 起始态须先无动画落位，再注入过渡才能触发淡入上浮。
        SettingsHeaderText.Transitions = null;
        SettingsHeaderText.Opacity = 0;
        SettingsHeaderText.RenderTransform = PageTransition.HeaderEnterFromTransform;
        SettingsContentPanel.Transitions = null;
        SettingsContentPanel.Opacity = 0;
        SettingsContentPanel.RenderTransform = PageTransition.EnterFromTransform;

        RequestSubPageEnterFrame(version);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (version == _subPageAnimationVersion && TopLevel.GetTopLevel(this) is not null)
                {
                    _pointeroverSuppressor.Apply();
                }
            },
            DispatcherPriority.Background);
    }

    private void RequestSubPageEnterFrame(long version)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            if (version == _subPageAnimationVersion)
            {
                RestoreSubPageVisualState();
            }

            return;
        }

        topLevel.RequestAnimationFrame(
            _ =>
            {
                if (version != _subPageAnimationVersion)
                {
                    return;
                }

                SettingsHeaderText.Transitions = PageTransition.CreateHeaderEnterTransitions();
                SettingsHeaderText.Opacity = 1;
                SettingsHeaderText.RenderTransform = PageTransition.HeaderRestTransform;
                SettingsContentPanel.Transitions = PageTransition.CreateEnterTransitions();
                SettingsContentPanel.Opacity = 1;
                SettingsContentPanel.RenderTransform = PageTransition.RestTransform;
                _pointeroverSuppressor.Apply();
            });
    }

    private void RestoreSubPageVisualState()
    {
        SettingsHeaderText.Transitions = null;
        SettingsHeaderText.Opacity = 1;
        SettingsHeaderText.RenderTransform = PageTransition.HeaderRestTransform;
        SettingsContentPanel.Transitions = null;
        SettingsContentPanel.Opacity = 1;
        SettingsContentPanel.RenderTransform = PageTransition.RestTransform;
        _pointeroverSuppressor.Reset();
    }

    // 设置页常驻不卸载；退订仅覆盖热重载重建旧实例时防订阅泄漏。
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromVisualTree(args);
        _subPageAnimationVersion++;
        RestoreSubPageVisualState();
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _settings = null;
            _viewModel = null;
        }
    }
}
