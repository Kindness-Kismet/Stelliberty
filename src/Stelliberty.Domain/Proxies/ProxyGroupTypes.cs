namespace Stelliberty.Domain.Proxies;

public static class ProxyGroupTypes
{
    public const string Select = "select";
    public const string Selector = "selector";
    public const string UrlTest = "url-test";
    public const string Fallback = "fallback";
    public const string LoadBalance = "load-balance";

    public static bool IsManualSelectable(string type) =>
        IsSelect(type) || UsesFixedSelection(type);

    public static bool RequiresDefaultSelection(string type) => IsSelect(type);

    public static bool UsesFixedSelection(string type) =>
        IsUrlTest(type) || IsFallback(type);

    // url-test 的固定不会自行失效，只能显式清除；fallback 在固定节点不可用时由核心自动清除。
    public static bool KeepsFixedSelectionUntilCleared(string type) => IsUrlTest(type);

    private static bool IsSelect(string type) =>
        string.Equals(type, Select, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, Selector, StringComparison.OrdinalIgnoreCase);

    private static bool IsUrlTest(string type) =>
        string.Equals(type, UrlTest, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "URLTest", StringComparison.OrdinalIgnoreCase);

    private static bool IsFallback(string type) =>
        string.Equals(type, Fallback, StringComparison.OrdinalIgnoreCase);
}
