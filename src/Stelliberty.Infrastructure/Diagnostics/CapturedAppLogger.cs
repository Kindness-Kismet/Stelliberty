using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Diagnostics;

public sealed class CapturedAppLogger : IAppLogger
{
    private const int Capacity = 1000;
    // 文件保留 1 万行；超过余量阈值才压缩。
    private const int FileMaxLines = 10000;
    private const int FileCompactionSlack = 2000;

    private readonly Queue<AppLogEntry> _entries = new(Capacity);
    private readonly object _syncRoot = new();
    private readonly string? _logFilePath;
    private int _fileLineCount;

    public CapturedAppLogger(string? logFilePath = null)
    {
        _logFilePath = string.IsNullOrWhiteSpace(logFilePath) ? null : logFilePath;
        InitializeLogFile();
    }

    public event EventHandler<AppLogEntry>? EntryWritten;

    public IReadOnlyList<AppLogEntry> Snapshot()
    {
        lock (_syncRoot)
        {
            return _entries.ToArray();
        }
    }

    public void Debug(string message) => Write(AppLogLevel.Debug, message);

    public void Info(string message) => Write(AppLogLevel.Info, message);

    public void Warning(string message) => Write(AppLogLevel.Warning, message);

    public void Error(string message) => Write(AppLogLevel.Error, message);

    public void Error(Exception exception, string message) => Write(AppLogLevel.Error, $"{message}: {exception}");

    private void Write(AppLogLevel level, string message)
    {
        var entry = new AppLogEntry(level, DateTime.Now, AppLogSanitizer.Sanitize(message));

        lock (_syncRoot)
        {
            while (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
            AppendToFile(entry);
        }

        WriteConsole(entry);
        System.Diagnostics.Debug.WriteLine(entry.Format());
        EntryWritten?.Invoke(this, entry);
    }

    // 文件写入串行执行；IO 失败不能影响业务流程。
    private void InitializeLogFile()
    {
        if (_logFilePath is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _fileLineCount = File.Exists(_logFilePath) ? File.ReadLines(_logFilePath).Count() : 0;
            CompactLogFileIfNeeded(force: _fileLineCount > FileMaxLines);
        }
        catch
        {
        }
    }

    private void AppendToFile(AppLogEntry entry)
    {
        if (_logFilePath is null)
        {
            return;
        }

        try
        {
            var line = entry.Format();
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
            // 条目可能跨多行，尤其是异常堆栈。
            _fileLineCount += CountLines(line);
            CompactLogFileIfNeeded(force: false);
        }
        catch
        {
            // 保存失败不能影响业务日志管线。
        }
    }

    private void CompactLogFileIfNeeded(bool force)
    {
        if (_logFilePath is null || (!force && _fileLineCount <= FileMaxLines + FileCompactionSlack))
        {
            return;
        }

        try
        {
            var allLines = File.ReadAllLines(_logFilePath);
            if (allLines.Length <= FileMaxLines)
            {
                _fileLineCount = allLines.Length;
                return;
            }

            var kept = allLines[^FileMaxLines..];
            File.WriteAllLines(_logFilePath, kept);
            _fileLineCount = kept.Length;
        }
        catch
        {
            // 压缩失败时保留原文件，留待下次重试。
        }
    }

    private static int CountLines(string text)
    {
        var lines = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    private static void WriteConsole(AppLogEntry entry)
    {
        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = GetConsoleColor(entry.Level);
        Console.Write(entry.LevelText);
        Console.ForegroundColor = previousColor;
        Console.WriteLine($" {entry.Text}");
    }

    private static ConsoleColor GetConsoleColor(AppLogLevel level)
    {
        return level switch
        {
            AppLogLevel.Debug => ConsoleColor.DarkGray,
            AppLogLevel.Info => ConsoleColor.Cyan,
            AppLogLevel.Warning => ConsoleColor.Yellow,
            AppLogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }
}
