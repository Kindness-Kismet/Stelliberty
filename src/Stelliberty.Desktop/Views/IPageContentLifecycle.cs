namespace Stelliberty.Desktop.Views;

internal interface IPageContentLifecycle
{
    void WarmupPageContent();

    void ActivatePageContent();

    void DeactivatePageContent();

    void ReleasePageContent();
}
