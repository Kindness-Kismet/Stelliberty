namespace Stelliberty.Desktop.Views;

internal interface IPageContentLifecycle
{
    void ActivatePageContent();

    void DeactivatePageContent();

    void ReleasePageContent();
}
