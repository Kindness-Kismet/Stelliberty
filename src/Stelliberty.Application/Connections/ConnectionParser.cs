using Stelliberty.Domain.Connections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stelliberty.Application.Connections;

public sealed class ConnectionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<ConnectionInfo> Parse(string content, DateTimeOffset fallbackStart)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("connections", out var connectionsElement)
                || connectionsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var connections = new List<ConnectionInfo>();
            foreach (var connectionElement in connectionsElement.EnumerateArray())
            {
                var connection = TryParseConnection(connectionElement, fallbackStart);
                if (connection is not null)
                {
                    connections.Add(connection);
                }
            }

            return connections;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ConnectionInfo? TryParseConnection(JsonElement element, DateTimeOffset fallbackStart)
    {
        try
        {
            var payload = element.Deserialize<ConnectionPayload>(JsonOptions);
            return payload is null ? null : ToConnection(payload, fallbackStart);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ConnectionInfo ToConnection(ConnectionPayload payload, DateTimeOffset fallbackStart)
    {
        return new ConnectionInfo(
            Id: payload.Id,
            Upload: payload.Upload,
            Download: payload.Download,
            UploadSpeed: payload.UploadSpeed,
            DownloadSpeed: payload.DownloadSpeed,
            Start: payload.Start ?? fallbackStart,
            Metadata: ToMetadata(payload.Metadata),
            Chains: payload.Chains,
            Rule: payload.Rule,
            RulePayload: payload.RulePayload);
    }

    private static ConnectionMetadata ToMetadata(ConnectionMetadataPayload payload)
    {
        return new ConnectionMetadata(
            Type: payload.Type,
            Network: payload.Network,
            SourceIp: payload.SourceIp,
            SourcePort: payload.SourcePort,
            SourceGeoIp: payload.SourceGeoIp,
            SourceIpAsn: payload.SourceIpAsn,
            DestinationIp: payload.DestinationIp,
            DestinationPort: payload.DestinationPort,
            DestinationGeoIp: payload.DestinationGeoIp,
            DestinationIpAsn: payload.DestinationIpAsn,
            Host: payload.Host,
            SniffHost: payload.SniffHost,
            Process: payload.Process,
            ProcessPath: payload.ProcessPath,
            Uid: payload.Uid,
            InboundIp: payload.InboundIp,
            InboundPort: payload.InboundPort,
            InboundName: payload.InboundName,
            InboundUser: payload.InboundUser,
            Dscp: payload.Dscp,
            RemoteDestination: payload.RemoteDestination,
            DnsMode: payload.DnsMode,
            SpecialProxy: payload.SpecialProxy,
            SpecialRules: payload.SpecialRules);
    }

    private sealed class ConnectionsPayload
    {
        public IReadOnlyList<ConnectionPayload> Connections { get; set; } = [];
    }

    private sealed class ConnectionPayload
    {
        public string Id { get; set; } = string.Empty;

        [JsonConverter(typeof(LongValueJsonConverter))]
        public long Upload { get; set; }

        [JsonConverter(typeof(LongValueJsonConverter))]
        public long Download { get; set; }

        [JsonConverter(typeof(LongValueJsonConverter))]
        public long UploadSpeed { get; set; }

        [JsonConverter(typeof(LongValueJsonConverter))]
        public long DownloadSpeed { get; set; }

        [JsonConverter(typeof(NullableDateTimeOffsetJsonConverter))]
        public DateTimeOffset? Start { get; set; }

        public ConnectionMetadataPayload Metadata { get; set; } = new();

        [JsonConverter(typeof(StringListJsonConverter))]
        public IReadOnlyList<string> Chains { get; set; } = [];

        public string Rule { get; set; } = string.Empty;

        public string RulePayload { get; set; } = string.Empty;
    }

    private sealed class ConnectionMetadataPayload
    {
        public string Type { get; set; } = string.Empty;

        public string Network { get; set; } = string.Empty;

        [JsonPropertyName("sourceIP")]
        public string SourceIp { get; set; } = string.Empty;

        [JsonConverter(typeof(StringValueJsonConverter))]
        public string SourcePort { get; set; } = string.Empty;

        [JsonPropertyName("sourceGeoIP")]
        [JsonConverter(typeof(StringListJsonConverter))]
        public IReadOnlyList<string> SourceGeoIp { get; set; } = [];

        [JsonPropertyName("sourceIPASN")]
        public string SourceIpAsn { get; set; } = string.Empty;

        [JsonPropertyName("destinationIP")]
        public string DestinationIp { get; set; } = string.Empty;

        [JsonConverter(typeof(StringValueJsonConverter))]
        public string DestinationPort { get; set; } = string.Empty;

        [JsonPropertyName("destinationGeoIP")]
        [JsonConverter(typeof(StringListJsonConverter))]
        public IReadOnlyList<string> DestinationGeoIp { get; set; } = [];

        [JsonPropertyName("destinationIPASN")]
        public string DestinationIpAsn { get; set; } = string.Empty;

        public string Host { get; set; } = string.Empty;

        public string SniffHost { get; set; } = string.Empty;

        public string Process { get; set; } = string.Empty;

        public string ProcessPath { get; set; } = string.Empty;

        [JsonConverter(typeof(NullableIntValueJsonConverter))]
        public int? Uid { get; set; }

        [JsonPropertyName("inboundIP")]
        public string InboundIp { get; set; } = string.Empty;

        [JsonConverter(typeof(StringValueJsonConverter))]
        public string InboundPort { get; set; } = string.Empty;

        public string InboundName { get; set; } = string.Empty;

        public string InboundUser { get; set; } = string.Empty;

        [JsonConverter(typeof(IntValueJsonConverter))]
        public int Dscp { get; set; }

        public string RemoteDestination { get; set; } = string.Empty;

        public string DnsMode { get; set; } = string.Empty;

        public string SpecialProxy { get; set; } = string.Empty;

        public string SpecialRules { get; set; } = string.Empty;
    }

    private sealed class NullableDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            {
                return value;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private sealed class NullableIntValueJsonConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return IntValueReader.Read(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private sealed class IntValueJsonConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return IntValueReader.Read(ref reader) ?? 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private sealed class LongValueJsonConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return LongValueReader.Read(ref reader) ?? 0;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private sealed class StringValueJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return StringValueReader.Read(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private sealed class StringListJsonConverter : JsonConverter<IReadOnlyList<string>>
    {
        public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return [];
            }

            var values = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                values.Add(StringValueReader.Read(ref reader));
            }

            return values;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private static class IntValueReader
    {
        public static int? Read(ref Utf8JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.TryGetInt32(out var value) ? value : null,
                JsonTokenType.String => int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null,
                _ => null
            };
        }
    }

    private static class LongValueReader
    {
        public static long? Read(ref Utf8JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.TryGetInt64(out var value) ? value : null,
                JsonTokenType.String => long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null,
                _ => null
            };
        }
    }

    private static class StringValueReader
    {
        public static string Read(ref Utf8JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.TryGetInt64(out var number) ? number.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => "null",
                _ => JsonDocument.ParseValue(ref reader).RootElement.ToString()
            };
        }
    }
}
