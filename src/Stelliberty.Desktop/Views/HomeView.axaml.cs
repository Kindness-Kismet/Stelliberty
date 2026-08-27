using Avalonia.Controls;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class HomeView : UserControl, IPageContentLifecycle
{
    private bool _isPageContentLive = true;

    public HomeView()
    {
        InitializeComponent();
    }

    void IPageContentLifecycle.ActivatePageContent() => ApplySpeedChartLive(true);

    void IPageContentLifecycle.DeactivatePageContent() => ApplySpeedChartLive(false);

    void IPageContentLifecycle.ReleasePageContent() => ApplySpeedChartLive(false);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // 视图重建时 DataContext 晚于激活回调到位，需补一次同步。
        ApplySpeedChartLive(_isPageContentLive);
    }

    private void ApplySpeedChartLive(bool isLive)
    {
        _isPageContentLive = isLive;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.HomePage.SetSpeedChartLive(isLive);
        }
    }
}
