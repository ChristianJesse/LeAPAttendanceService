using System.Runtime.InteropServices;
using LeAPAttendanceService.Models;
using Microsoft.Extensions.Options;

namespace LeAPAttendanceService.Services;

public sealed class ZkemkeeperBiometricService : IBiometricService
{
    private static readonly string[] CandidateProgIds =
    [
        "zkemkeeper.ZKEM",
        "zkemkeeper.ZKEM.1",
        "zkemkeeper.CZKEM",
        "zkemkeeper.IZKEM",
        "zk.IZKEM",
        "ZKEMKEEPER.UIDLL",
        "zkemkeeper.UIDLL"
    ];

    private readonly ILogger<ZkemkeeperBiometricService> _logger;
    private readonly BiometricOptions _options;

    public ZkemkeeperBiometricService(
        ILogger<ZkemkeeperBiometricService> logger,
        IOptions<BiometricOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task<IReadOnlyList<BiometricAttendanceRecord>> GetAttendanceLogsAsync(DateTime fromInclusive, CancellationToken cancellationToken)
    {
        return Task.Run(() => ReadAttendanceLogs(fromInclusive), cancellationToken);
    }

    public Task SyncDeviceTimeAsync(CancellationToken cancellationToken)
    {
        return Task.Run(SyncDeviceTime, cancellationToken);
    }

    private IReadOnlyList<BiometricAttendanceRecord> ReadAttendanceLogs(DateTime fromInclusive)
    {
        ValidateOptions();

        var records = new List<BiometricAttendanceRecord>();
        ExecuteWithConnectedDevice(zk =>
        {
            // The device is disabled briefly while we read logs so the SDK can read a stable snapshot.
            zk.EnableDevice(_options.MachineNumber, false);

            if (!zk.ReadGeneralLogData(_options.MachineNumber))
            {
                throw new InvalidOperationException("The device connection succeeded, but it did not return attendance logs.");
            }

            BiometricAttendanceRecord record;
            while (TryReadSingleRecord(zk, _options.MachineNumber, out record))
            {
                if (record.Timestamp >= fromInclusive)
                {
                    records.Add(record);
                }
            }
        });

        return records.OrderBy(record => record.Timestamp).ToList();
    }

    private void SyncDeviceTime()
    {
        ValidateOptions();

        ExecuteWithConnectedDevice(zk =>
        {
            if (!zk.SetDeviceTime(_options.MachineNumber))
            {
                throw new InvalidOperationException(
                    $"Connected to {_options.DeviceIp}:{_options.Port}, but the device clock could not be synchronized.");
            }

            _logger.LogInformation(
                "Device time synchronized successfully for {DeviceIp}:{Port}.",
                _options.DeviceIp,
                _options.Port);
        });
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.DeviceIp))
        {
            throw new InvalidOperationException("Biometric device IP is missing. Set Biometric:DeviceIp in appsettings.json.");
        }

        if (_options.Port <= 0)
        {
            throw new InvalidOperationException("Biometric port must be greater than zero.");
        }
    }

    private object CreateComObject()
    {
        foreach (var progId in CandidateProgIds)
        {
            try
            {
                var comType = Type.GetTypeFromProgID(progId, throwOnError: false);
                if (comType is null)
                {
                    continue;
                }

                var instance = Activator.CreateInstance(comType);
                if (instance is not null)
                {
                    _logger.LogInformation("Created ZKTeco SDK COM object using ProgID {ProgId}.", progId);
                    return instance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to create ZKTeco SDK COM object using ProgID {ProgId}.", progId);
            }
        }

        var sdkFolder = Path.GetFullPath(_options.SdkFolder, AppContext.BaseDirectory);

        throw new InvalidOperationException(
            "Failed to create the ZKTeco SDK COM object. " +
            $"Make sure zkemkeeper.dll is registered on this machine. SDK folder checked: {sdkFolder}. " +
            "You can use Scripts\\Register-ZkSdk.ps1 as Administrator to install and register the SDK.");
    }

    private void ExecuteWithConnectedDevice(Action<dynamic> action)
    {
        object? comObject = null;
        dynamic? zk = null;

        try
        {
            comObject = CreateComObject();
            zk = comObject;

            if (!zk.Connect_Net(_options.DeviceIp, _options.Port))
            {
                throw new InvalidOperationException(
                    $"Unable to connect to device at {_options.DeviceIp}:{_options.Port}. Check the IP, port, and firewall rules.");
            }

            action(zk);
        }
        finally
        {
            // Re-enable the device and disconnect even when something fails,
            // because leaving the device disabled would block staff from clocking in or out.
            TryInvoke(() => zk?.EnableDevice(_options.MachineNumber, true));
            TryInvoke(() => zk?.Disconnect());

            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
    }

    private static bool TryReadSingleRecord(dynamic zk, int machineNumber, out BiometricAttendanceRecord record)
    {
        string enrollNumber = string.Empty;
        int verifyMode = 0;
        int inOutMode = 0;
        int year = 0;
        int month = 0;
        int day = 0;
        int hour = 0;
        int minute = 0;
        int second = 0;
        int workCode = 0;

        var hasRecord = zk.SSR_GetGeneralLogData(
            machineNumber,
            out enrollNumber,
            out verifyMode,
            out inOutMode,
            out year,
            out month,
            out day,
            out hour,
            out minute,
            out second,
            ref workCode);

        if (!hasRecord)
        {
            record = default!;
            return false;
        }

        record = new BiometricAttendanceRecord(
            EmployeeId: enrollNumber,
            Timestamp: new DateTime(year, month, day, hour, minute, second),
            InOutStatus: MapInOutStatus(inOutMode),
            VerifyMode: verifyMode,
            WorkCode: workCode);

        return true;
    }

    private static string MapInOutStatus(int inOutMode)
    {
        return inOutMode switch
        {
            0 => "IN",
            1 => "OUT",
            2 => "BREAK_IN",
            3 => "BREAK_OUT",
            4 => "OVERTIME_IN",
            5 => "OVERTIME_OUT",
            _ => "UNKNOWN"
        };
    }

    private static void TryInvoke(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Cleanup should not hide the original error from the biometric read.
        }
    }
}
