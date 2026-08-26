namespace Stelliberty.Infrastructure.Storage;

// 同目录临时文件写完后原子替换，进程终止或磁盘故障不会留下截断文件
public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
