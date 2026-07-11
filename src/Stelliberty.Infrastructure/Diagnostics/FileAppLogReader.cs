using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Diagnostics;

public sealed partial class FileAppLogReader(string logFilePath) : IAppLogReader
{
    private const int InitialTailBytes = 64 * 1024;
    private const int MaxTailBytes = 1024 * 1024;

    public IReadOnlyList<AppLogEntry> ReadEntries(int maxEntries, CancellationToken cancellationToken = default)
    {
        if (maxEntries <= 0 || string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
        {
            return [];
        }

        using var stream = new FileStream(
            logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (stream.Length == 0)
        {
            return [];
        }

        var length = stream.Length;
        var maxTailBytes = Math.Min(length, MaxTailBytes);
        var tailBytes = Math.Min(length, InitialTailBytes);
        IReadOnlyList<AppLogEntry> entries;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries = ReadTailEntries(stream, length, tailBytes, maxEntries, cancellationToken);
            if (entries.Count >= maxEntries || tailBytes >= maxTailBytes)
            {
                return entries;
            }

            tailBytes = Math.Min(maxTailBytes, tailBytes * 2);
        }
        while (tailBytes < length);

        return entries;
    }

    private static IReadOnlyList<AppLogEntry> ReadTailEntries(
        FileStream stream,
        long length,
        long tailBytes,
        int maxEntries,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[(int)tailBytes];
        stream.Seek(length - tailBytes, SeekOrigin.Begin);
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        var content = Encoding.UTF8.GetString(buffer, 0, totalRead);
        return ParseEntries(content, maxEntries, cancellationToken);
    }

    private static IReadOnlyList<AppLogEntry> ParseEntries(string content, int maxEntries, CancellationToken cancellationToken)
    {
        var entries = new List<AppLogEntry>();
        AppLogLevel? level = null;
        DateTime timestamp = default;
        var message = new StringBuilder();

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = EntryLineRegex().Match(line);
            if (match.Success)
            {
                AddEntry(entries, level, timestamp, message);
                level = ParseLevel(match.Groups["level"].Value);
                if (!DateTime.TryParseExact(
                    match.Groups["time"].Value,
                    "yyyy/M/d HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out timestamp))
                {
                    level = null;
                    message.Clear();
                    continue;
                }

                message.Clear();
                message.Append(match.Groups["message"].Value);
                continue;
            }

            if (message.Length > 0)
            {
                message.AppendLine();
                message.Append(line);
            }
        }

        AddEntry(entries, level, timestamp, message);
        return entries.Count > maxEntries ? entries[^maxEntries..] : entries;
    }

    private static void AddEntry(List<AppLogEntry> entries, AppLogLevel? level, DateTime timestamp, StringBuilder message)
    {
        if (level is null)
        {
            return;
        }

        entries.Add(new AppLogEntry(level.Value, timestamp, message.ToString()));
    }

    private static AppLogLevel ParseLevel(string level) => level switch
    {
        "D" => AppLogLevel.Debug,
        "W" => AppLogLevel.Warning,
        "E" => AppLogLevel.Error,
        _ => AppLogLevel.Info
    };

    [GeneratedRegex(@"^\[(?<level>[DIWE])\]\s(?<time>\d{4}/\d{1,2}/\d{1,2}\s\d{2}:\d{2}:\d{2})\s(?<message>.*)$")]
    private static partial Regex EntryLineRegex();
}
