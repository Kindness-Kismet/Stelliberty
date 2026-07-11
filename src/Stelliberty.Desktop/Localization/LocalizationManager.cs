using Stelliberty.Application.Localization;

namespace Stelliberty.Desktop.Localization;

public static class LocalizationManager
{
    private static ILocalizationService? _service;

    public static void Initialize(ILocalizationService service) => _service = service;

    public static string Translate(string key) => _service?.GetString(key) ?? key;

    public static IObservable<string> Observe(string key) => new KeyObservable(key);

    private sealed class KeyObservable(string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            var service = _service;
            observer.OnNext(service?.GetString(key) ?? key);
            if (service is null)
            {
                return EmptySubscription.Instance;
            }

            void Handler(object? sender, EventArgs args) => observer.OnNext(service.GetString(key));
            service.LanguageChanged += Handler;
            return new EventSubscription(() => service.LanguageChanged -= Handler);
        }
    }

    private sealed class EventSubscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static readonly EmptySubscription Instance = new();

        public void Dispose()
        {
        }
    }
}
