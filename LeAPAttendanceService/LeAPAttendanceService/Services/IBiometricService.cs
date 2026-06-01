using LeAPAttendanceService.Models;

namespace LeAPAttendanceService.Services;

public interface IBiometricService
{
    Task SyncDeviceTimeAsync(LeapBiometricServer server, CancellationToken cancellationToken);

    Task<IReadOnlyList<BiometricAttendanceRecord>> GetAttendanceLogsAsync(LeapBiometricServer server, DateTime fromInclusive, CancellationToken cancellationToken);
}
