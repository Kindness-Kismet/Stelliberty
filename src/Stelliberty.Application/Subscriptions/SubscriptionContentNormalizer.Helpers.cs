using Stelliberty.Domain.Subscriptions;
using System.Text;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed partial class SubscriptionContentNormalizer
{
    private static string DecodeBase64IfNeeded(string value)
    {
        var unescaped = Uri.UnescapeDataString(value);
        if (unescaped.Contains(':', StringComparison.Ordinal))
        {
            return unescaped;
        }

        return DecodeBase64Text(value, value);
    }

    private static string DecodeBase64Text(string value, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(NormalizeBase64Input(value)));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static string NormalizeBase64Input(string value)
    {
        var normalized = new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray())
            .Replace('-', '+')
            .Replace('_', '/');
        return normalized.Length % 4 == 0
            ? normalized
            : normalized.PadRight(normalized.Length + 4 - normalized.Length % 4, '=');
    }

    private static bool IsBase64Character(char value)
    {
        return char.IsLetterOrDigit(value) || value is '+' or '/' or '-' or '_' or '=';
    }

    private static string JsonString(JsonElement root, string key, string fallback = "")
    {
        return root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex > 0)
            {
                result[pair[..separatorIndex]] = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            }
        }

        return result;
    }

    private static string JsonIntString(JsonElement root, string key, string fallback)
    {
        if (!root.TryGetProperty(key, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number.ToString(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number.ToString(),
            _ => fallback
        };
    }

    private static int IntQuery(IReadOnlyDictionary<string, string> query, string key, int fallback)
    {
        return int.TryParse(query.GetValueOrDefault(key), out var value) ? value : fallback;
    }

    private static bool BoolQuery(IReadOnlyDictionary<string, string> query, string key)
    {
        var value = query.GetValueOrDefault(key, string.Empty);
        return value == "1" || bool.TryParse(value, out var parsed) && parsed;
    }

    private static string V2RayNetwork(IReadOnlyDictionary<string, string> query)
    {
        var network = query.GetValueOrDefault("type", "tcp").ToLowerInvariant();
        var fakeType = query.GetValueOrDefault("headerType", string.Empty).ToLowerInvariant();
        return network switch
        {
            "tcp" when fakeType == "http" => "http",
            "http" => "h2",
            "" => "tcp",
            _ => network
        };
    }

    private static void Set(YamlMappingNode mapping, string key, string value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static void SetIfNotBlank(YamlMappingNode mapping, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Set(mapping, key, value);
        }
    }

    private static void SetBoolIfTrue(YamlMappingNode mapping, string key, bool value)
    {
        if (value)
        {
            Set(mapping, key, "true");
        }
    }

    private static void SetAlpn(YamlMappingNode mapping, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Set(mapping, "alpn", Sequence(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }
    }

    private static void SetV2RayTransportOptions(YamlMappingNode proxy, string network, IReadOnlyDictionary<string, string> query)
    {
        switch (network)
        {
            case "ws":
            case "httpupgrade":
                var wsOptions = new YamlMappingNode();
                Set(wsOptions, "path", query.GetValueOrDefault("path", "/"));
                SetIfNotBlank(wsOptions, "early-data-header-name", query.GetValueOrDefault("eh", string.Empty));
                if (int.TryParse(query.GetValueOrDefault("ed"), out var earlyData))
                {
                    Set(wsOptions, "max-early-data", earlyData.ToString());
                }

                var host = query.GetValueOrDefault("host", string.Empty);
                if (!string.IsNullOrWhiteSpace(host))
                {
                    var headers = new YamlMappingNode();
                    Set(headers, "Host", host);
                    Set(wsOptions, "headers", headers);
                }

                Set(proxy, "ws-opts", wsOptions);
                break;
            case "grpc":
                var grpcOptions = new YamlMappingNode();
                Set(grpcOptions, "grpc-service-name", query.GetValueOrDefault("serviceName", string.Empty));
                Set(proxy, "grpc-opts", grpcOptions);
                break;
            case "h2":
                var h2Options = new YamlMappingNode();
                Set(h2Options, "path", query.GetValueOrDefault("path", "/"));
                if (!string.IsNullOrWhiteSpace(query.GetValueOrDefault("host")))
                {
                    Set(h2Options, "host", Sequence(query["host"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                }

                Set(proxy, "h2-opts", h2Options);
                break;
            case "http":
                var httpOptions = new YamlMappingNode();
                Set(httpOptions, "path", Sequence([query.GetValueOrDefault("path", "/")]));
                SetIfNotBlank(httpOptions, "method", query.GetValueOrDefault("method", string.Empty));
                if (!string.IsNullOrWhiteSpace(query.GetValueOrDefault("host")))
                {
                    var headers = new YamlMappingNode();
                    Set(headers, "Host", Sequence(query["host"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                    Set(httpOptions, "headers", headers);
                }

                Set(proxy, "http-opts", httpOptions);
                break;
        }
    }

    private static void Set(YamlMappingNode mapping, string key, YamlNode value)
    {
        mapping.Children[new YamlScalarNode(key)] = value;
    }

    private static YamlSequenceNode Sequence(IEnumerable<string> values)
    {
        return new YamlSequenceNode(values.Select(value => new YamlScalarNode(value)));
    }
}
