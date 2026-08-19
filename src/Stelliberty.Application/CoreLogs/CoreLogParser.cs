using Stelliberty.Domain.CoreLogs;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stelliberty.Application.CoreLogs;

public sealed class CoreLogParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<CoreLogMessage> Parse(string content, DateTimeOffset fallbackTimestamp)
    {
        var text = content.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        if (text[0] is '{' or '[')
        {
            return ParseJson(text, fallbackTimestamp);
        }

        return [ParseTextLine(text, fallbackTimestamp)];
    }

    private static IReadOnlyList<CoreLogMessage> ParseJson(string content, DateTimeOffset fallbackTimestamp)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                    .Select(element => TryParseJsonMessage(element, fallbackTimestamp))
                    .OfType<CoreLogMessage>()
                    .ToList()
                : TryParseJsonMessage(document.RootElement, fallbackTimestamp) is { } message ? [message] : [];
        }
        catch (JsonException)
        {
            return [ParseTextLine(content, fallbackTimestamp)];
        }
    }

    private static CoreLogMessage ParseTextLine(string line, DateTimeOffset fallbackTimestamp)
    {
        var values = ParseKeyValues(line);
        var type = values.GetValueOrDefault("level")?.ToUpperInvariant() ?? "INFO";
        var payload = values.GetValueOrDefault("msg") ?? values.GetValueOrDefault("payload") ?? line;
        var timestamp = values.TryGetValue("time", out var time)
            && DateTimeOffset.TryParse(time, out var parsed)
                ? parsed
                : fallbackTimestamp;
        return new CoreLogMessage(type, payload, timestamp);
    }

    private static CoreLogMessage? TryParseJsonMessage(JsonElement element, DateTimeOffset fallbackTimestamp)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var payload = element.Deserialize<CoreLogPayload>(JsonOptions) ?? new CoreLogPayload();
            var timestamp = payload.Timestamp == default ? fallbackTimestamp : payload.Timestamp;
            return new CoreLogMessage(payload.TypeText, payload.PayloadText, timestamp);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseKeyValues(string line)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < line.Length)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            var keyStart = index;
            while (index < line.Length && line[index] != '=' && !char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (keyStart == index)
            {
                index++;
                continue;
            }

            var key = line[keyStart..index];
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (index >= line.Length || line[index] != '=')
            {
                continue;
            }

            index++;
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            values[key] = index < line.Length && line[index] == '"'
                ? ReadQuotedValue(line, ref index)
                : ReadBareValue(line, ref index);
        }

        return values;
    }

    private static string ReadQuotedValue(string line, ref int index)
    {
        index++;
        var builder = new StringBuilder();
        var escaped = false;
        while (index < line.Length)
        {
            var ch = line[index++];
            if (escaped)
            {
                builder.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                break;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string ReadBareValue(string line, ref int index)
    {
        var start = index;
        while (index < line.Length && !char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return line[start..index];
    }

    private sealed class CoreLogPayload
    {
        public string? Type { get; set; }

        public string? Level { get; set; }

        public string? Payload { get; set; }

        [JsonPropertyName("msg")]
        public string? Message { get; set; }

        [JsonPropertyName("time")]
        public DateTimeOffset Timestamp { get; set; }

        public string TypeText => (Type ?? Level ?? "INFO").ToUpperInvariant();

        public string PayloadText => Payload ?? Message ?? string.Empty;
    }
}
