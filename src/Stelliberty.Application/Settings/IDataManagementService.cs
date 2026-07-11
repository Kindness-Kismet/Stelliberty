namespace Stelliberty.Application.Settings;

public interface IDataManagementService
{
    DataManagementOperationResult CreateBackup();

    DataManagementOperationResult CreateBackup(string backupPath);

    DataManagementOperationResult RestoreBackup(DataRestoreMode mode);

    DataManagementOperationResult RestoreBackup(string backupPath, DataRestoreMode mode);
}
