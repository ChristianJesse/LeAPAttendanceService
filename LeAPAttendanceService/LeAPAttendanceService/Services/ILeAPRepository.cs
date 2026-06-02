using LeAPAttendanceService.Models;

namespace LeAPAttendanceService.Services;

public interface ILeAPRepository
{
    Task<IReadOnlyList<LeapBiometricServer>> fnGetBiometricServersAsync(CancellationToken cancellationToken);

    Task fnSaveAttendanceRecordsAsync(int serverId, IEnumerable<BiometricAttendanceRecord> records, CancellationToken cancellationToken);

    Task<int> fnGetBioInterval(CancellationToken cancellationToken);

    Task<int> fnGetBioLookBack(CancellationToken cancellationToken);

    Task fnSaveBiometricLogs(int serverId, int error, string type, string message, CancellationToken cancellationToken);
}
