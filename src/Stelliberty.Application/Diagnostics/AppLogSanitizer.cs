using System.Text.RegularExpressions;

namespace Stelliberty.Application.Diagnostics;

public static class AppLogSanitizer
{
    private const string Mask = "<redacted>";
    private const int MaxMessageLength = 6000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex HttpUrlRegex = new(
        @"\bhttps?://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ProxyUriRegex = new(
        @"\b(?:ss|ssr|vmess|vless|trojan|hysteria2|hy2|tuic|socks|socks5|snell)://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex AuthorizationHeaderRegex = new(
        @"(?<prefix>\b(?:authorization|proxy-authorization)\s*[:=]\s*)(?<value>[^,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex BearerTokenRegex = new(
        @"(?<prefix>\b(?:bearer|basic)\s+)[A-Za-z0-9._~+/=-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex SensitiveKeyValueRegex = new(
        @"(?<prefix>\b(?:access[-_]?token|refresh[-_]?token|id[-_]?token|token|secret|password|passwd|pwd|api[-_]?key|apikey|authorization|auth|user[-_]?agent|ua|url|source)\b\s*[:=]\s*)(?<quote>[""']?)(?<value>[^""'\s,;]+)(?<close>[""']?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var sanitized = Flatten(message);
        sanitized = SanitizePathPrefixes(sanitized);
        sanitized = HttpUrlRegex.Replace(sanitized, match => SanitizeHttpUrl(match.Value));
        sanitized = ProxyUriRegex.Replace(sanitized, match => SanitizeProxyUri(match.Value));
        sanitized = AuthorizationHeaderRegex.Replace(sanitized, match => $"{match.Groups["prefix"].Value}{Mask}");
        sanitized = BearerTokenRegex.Replace(sanitized, match => $"{match.Groups["prefix"].Value}{Mask}");
        sanitized = SensitiveKeyValueRegex.Replace(sanitized, match =>
        {
            var quote = match.Groups["quote"].Value;
            return $"{match.Groups["prefix"].Value}{quote}{Mask}{quote}";
        });
        sanitized = CollapseAdjacentMasks(sanitized);

        return sanitized.Length <= MaxMessageLength ? sanitized : sanitized[..MaxMessageLength] + "...";
    }

    private static string Flatten(string message)
    {
        return message
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string SanitizePathPrefixes(string message)
    {
        var sanitized = message;
        sanitized = ReplacePathPrefix(sanitized, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
        sanitized = ReplacePathPrefix(sanitized, Path.GetTempPath(), "%TEMP%");
        return sanitized;
    }

    private static string ReplacePathPrefix(string message, string path, string replacement)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return message;
        }

        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalized.Length == 0)
        {
            return message;
        }

        return message
            .Replace(normalized, replacement, StringComparison.OrdinalIgnoreCase)
            .Replace(normalized.Replace('\\', '/'), replacement, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeHttpUrl(string value)
    {
        var (core, suffix) = TrimTrailingPunctuation(value);
        if (!Uri.TryCreate(core, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return $"https://{Mask}{suffix}";
        }

        return $"{uri.Scheme}://{Mask}{suffix}";
    }

    private static string SanitizeProxyUri(string value)
    {
        var (core, suffix) = TrimTrailingPunctuation(value);
        var schemeEnd = core.IndexOf("://", StringComparison.Ordinal);
        var scheme = schemeEnd > 0 ? core[..schemeEnd] : "proxy";
        return $"{scheme}://{Mask}{suffix}";
    }

    private static (string Core, string Suffix) TrimTrailingPunctuation(string value)
    {
        var end = value.Length;
        while (end > 0 && IsTrailingPunctuation(value[end - 1]))
        {
            end--;
        }

        return end == value.Length ? (value, string.Empty) : (value[..end], value[end..]);
    }

    private static bool IsTrailingPunctuation(char ch)
    {
        return ch is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}'
            or '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}';
    }

    private static string CollapseAdjacentMasks(string message)
    {
        var duplicate = Mask + Mask;
        while (message.Contains(duplicate, StringComparison.Ordinal))
        {
            message = message.Replace(duplicate, Mask, StringComparison.Ordinal);
        }

        return message;
    }
}
