using LeAPAttendanceService.Models;

namespace LeAPAttendanceService.Services;

public interface ILeAPRepository
{
    Task<IReadOnlyList<LeapBiometricServer>> GetBiometricServersAsync(CancellationToken cancellationToken);

    Task SaveAttendanceRecordsAsync(int serverId, IEnumerable<BiometricAttendanceRecord> records, CancellationToken cancellationToken);
}
