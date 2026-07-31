using System.Text.Json;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Storage;

// JSON 状态损坏时，记录日志、备份并返回默认值。
internal static class JsonFileRecovery
{
    public static T? ReadOrRecover<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"Read failed for {Path.GetFileName(path)}; keeping file: {exception.Message}");
            throw;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException exception)
        {
            AppLogger.Warning($"Parse failed for {Path.GetFileName(path)}; backing up corrupt file and rebuilding: {exception.Message}");
            BackupCorrupted(path);
            return default;
        }
    }

    // 固定备份后缀避免堆积；备份失败不阻止降级。
    private static void BackupCorrupted(string path)
    {
        try
        {
            var backupPath = path + ".corrupt";
            File.Delete(backupPath);
            File.Move(path, backupPath);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Corrupt file backup failed: {exception.Message}");
        }
    }
}
