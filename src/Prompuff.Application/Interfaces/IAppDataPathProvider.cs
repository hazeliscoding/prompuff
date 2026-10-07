namespace Prompuff.Application.Interfaces;

/// <summary>Every file Prompuff writes lives under these folders. Nothing goes next to the executable.</summary>
public interface IAppDataPathProvider
{
    string GetAppDataDirectory();
    string GetDatabasePath();
    string GetLogsDirectory();
    string GetBackupDirectory();
    string GetExportDirectory();
    string GetConfigDirectory();
    string GetSettingsPath();
}
