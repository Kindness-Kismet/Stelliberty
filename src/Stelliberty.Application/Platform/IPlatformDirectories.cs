namespace Stelliberty.Application.Platform;

public interface IPlatformDirectories
{
    string AppDataDirectory { get; }
    string DepsDirectory { get; }
    string CoreDirectory { get; }
    string RuntimeDirectory { get; }
    string SettingsFilePath { get; }
}
