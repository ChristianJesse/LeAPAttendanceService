using LeAPAttendanceService.Models;

namespace LeAPAttendanceService.Services;

public interface IBiometricService
{
    Task SyncDeviceTimeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BiometricAttendanceRecord>> GetAttendanceLogsAsync(DateTime fromInclusive, CancellationToken cancellationToken);
}
