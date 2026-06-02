using LeAPAttendanceService.Models;
using LeAPAttendanceService.Services;
using Microsoft.Extensions.Options;

namespace LeAPAttendanceService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IBiometricService _biometricService;
    private readonly ILeAPRepository _repository;
    private readonly BiometricOptions _biometricOptions;
    private readonly HashSet<string> _lastBatchKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, DateTime> _nextReadFromByServer = new();
    private readonly HashSet<int> _syncedServerIds = new();

    public Worker(
        ILogger<Worker> logger,
        IBiometricService biometricService,
        ILeAPRepository repository,
        IOptions<BiometricOptions> biometricOptions)
    {
        _logger = logger;
        _biometricService = biometricService;
        _repository = repository;
        _biometricOptions = biometricOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine(
            $"[LeAPAttendanceService] Worker started. PollingIntervalSeconds={_biometricOptions.PollingIntervalSeconds}, " +
            $"InitialLookBackMinutes={_biometricOptions.InitialLookBackMinutes}, SyncTimeOnStartup={_biometricOptions.SyncTimeOnStartup}");

        _logger.LogInformation(
            "LeAP Attendance worker started. PollingIntervalSeconds: {PollingIntervalSeconds}, InitialLookBackMinutes: {InitialLookBackMinutes}, SyncTimeOnStartup: {SyncTimeOnStartup}",
            _biometricOptions.PollingIntervalSeconds,
            _biometricOptions.InitialLookBackMinutes,
            _biometricOptions.SyncTimeOnStartup);

        if (!_biometricOptions.Enabled)
        {
            Console.WriteLine($"[LeAPAttendanceService] Biometric polling is disabled. Set {BiometricOptions.SectionName}:{nameof(BiometricOptions.Enabled)} to true.");
            _logger.LogInformation(
                "Biometric polling is disabled. Set {Section}:{Key} to true when the device details are ready.",
                BiometricOptions.SectionName,
                nameof(BiometricOptions.Enabled));
            return; 
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshDynamicBiometricSettingsAsync(stoppingToken);

            try
            {
                var servers = await _repository.fnGetBiometricServersAsync(stoppingToken);
                if (servers.Count == 0)
                {
                    var message = "No biometric servers were returned from stored procedure spLEAP with @pOption=38.";
                    Console.WriteLine($"[LeAPAttendanceService] {message}");
                    _logger.LogWarning("No biometric servers were returned from spLEAP(@pOption=38).");
                    await LogBiometricEventAsync(0, 0, "[Warning] - Empty Data", message, stoppingToken);
                }

                foreach (var server in servers)
                {
                    try
                    {
                        if (_biometricOptions.SyncTimeOnStartup && !_syncedServerIds.Contains(server.ServerID))
                        {
                            try
                            {
                                Console.WriteLine($"[LeAPAttendanceService] Synchronizing device time for {server.IPAddress}:{server.Port} (ServerID={server.ServerID}).");
                                await _biometricService.SyncDeviceTimeAsync(server, stoppingToken);
                                _syncedServerIds.Add(server.ServerID);
                                await LogBiometricEventAsync(server.ServerID, 0, "[Success] - Success", "Device time synchronized successfully.", stoppingToken);
                            }
                            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                            {
                                var errorType = $"[Error] - {ex.GetType().Name}";
                                Console.WriteLine($"[LeAPAttendanceService] Time sync failed for ServerID={server.ServerID}: {ex.Message}");
                                _logger.LogError(ex, "Time synchronization failed for biometric server {ServerID} at {IPAddress}:{Port}.", server.ServerID, server.IPAddress, server.Port);
                                await LogBiometricEventAsync(server.ServerID, 1, errorType, ex.ToString(), stoppingToken);
                            }
                        }

                        var fromTime = _nextReadFromByServer.GetValueOrDefault(server.ServerID,
                            DateTime.Now.AddMinutes(-Math.Abs(_biometricOptions.InitialLookBackMinutes)));

                        Console.WriteLine($"[LeAPAttendanceService] Starting biometric poll for ServerID={server.ServerID} from {fromTime:yyyy-MM-dd HH:mm:ss}.");
                        _logger.LogInformation(
                            "Starting biometric poll for server {ServerID} at {IPAddress}:{Port} from {FromTime:yyyy-MM-dd HH:mm:ss}.",
                            server.ServerID,
                            server.IPAddress,
                            server.Port,
                            fromTime);

                        var records = await _biometricService.GetAttendanceLogsAsync(server, fromTime, stoppingToken);
                        var newRecords = new List<BiometricAttendanceRecord>();

                        Console.WriteLine($"[LeAPAttendanceService] ServerID={server.ServerID} returned {records.Count} record(s).");
                        if (records.Count > 0)
                        {
                            Console.WriteLine("ServerID\tIDNumber\tInOutStatus\tTimeInOut");
                        }

                        foreach (var record in records)
                        {
                            var recordKey = $"{server.ServerID}|{record.UniqueKey}";
                            var isNew = _lastBatchKeys.Add(recordKey);
                            var recordState = isNew ? "NEW" : "EXISTING";

                            Console.WriteLine($"{server.ServerID}\t{record.EmployeeId}\t{record.InOutStatus}\t{record.Timestamp:yyyy-MM-dd HH:mm:ss.000}");
                            Console.WriteLine(
                                $"[LeAPAttendanceService] Device Data => ServerID={server.ServerID}, State={recordState}, EmployeeId={record.EmployeeId}, Status={record.InOutStatus}, " +
                                $"Timestamp={record.Timestamp:yyyy-MM-dd HH:mm:ss}, VerifyMode={record.VerifyMode}, WorkCode={record.WorkCode}");

                            if (isNew)
                            {
                                newRecords.Add(record);
                            }
                        }

                        if (newRecords.Count > 0)
                        {
                            await _repository.fnSaveAttendanceRecordsAsync(server.ServerID, newRecords, stoppingToken);
                            var successMessage = $"Successfully synchronized {newRecords.Count} attendance records.";
                            Console.WriteLine($"[LeAPAttendanceService] Saved {newRecords.Count} new record(s) for ServerID={server.ServerID} to spLEAP(@pOption=3).");
                            _logger.LogInformation(
                                "Saved {NewCount} attendance records for ServerID={ServerID}.",
                                newRecords.Count,
                                server.ServerID);
                            await LogBiometricEventAsync(server.ServerID, 0, "[Success] - Success", successMessage, stoppingToken);
                        }
                        else if (records.Count > 0)
                        {
                            var warningMessage = $"No new attendance records to save for ServerID={server.ServerID}; all {records.Count} returned rows are duplicates of already-processed data.";
                            Console.WriteLine($"[LeAPAttendanceService] {warningMessage}");
                            _logger.LogInformation(
                                "No new attendance records to save for ServerID={ServerID}; {RowCount} rows returned.",
                                server.ServerID,
                                records.Count);
                            await LogBiometricEventAsync(server.ServerID, 0, "[Warning] - DuplicateRecords", warningMessage, stoppingToken);
                        }

                        if (records.Count > 0)
                        {
                            _nextReadFromByServer[server.ServerID] = records.Max(record => record.Timestamp).AddSeconds(-1);
                        }
                        else
                        {
                            _nextReadFromByServer[server.ServerID] = DateTime.Now.AddSeconds(-1);
                            var warningMessage = $"No attendance records found for ServerID={server.ServerID} from {fromTime:yyyy-MM-dd HH:mm:ss}.";
                            Console.WriteLine($"[LeAPAttendanceService] {warningMessage}");
                            _logger.LogInformation(
                                "Biometric poll for server {ServerID} succeeded but returned no records.",
                                server.ServerID);
                            await LogBiometricEventAsync(server.ServerID, 0, "[Warning] - Empty Data", warningMessage, stoppingToken);
                        }
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        var errorType = $"[Error] - {ex.GetType().Name}";
                        Console.WriteLine($"[LeAPAttendanceService] Poll failed for ServerID={server.ServerID}: {ex.Message}");
                        _logger.LogError(ex, "Biometric poll failed for server {ServerID}.", server.ServerID);
                        await LogBiometricEventAsync(server.ServerID, 1, errorType, ex.ToString(), stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine($"[LeAPAttendanceService] Poll failed: {ex.Message}");
                _logger.LogError(ex, "Biometric poll loop failed.");
                await LogBiometricEventAsync(0, 1, $"[Error] - {ex.GetType().Name}", ex.ToString(), stoppingToken);
            }

            Console.WriteLine($"[LeAPAttendanceService] Waiting {Math.Max(5, _biometricOptions.PollingIntervalSeconds)} seconds before next poll.");
            _logger.LogInformation(
                "Waiting {DelaySeconds} seconds before the next biometric poll.",
                Math.Max(5, _biometricOptions.PollingIntervalSeconds));

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _biometricOptions.PollingIntervalSeconds)), stoppingToken);
        }
    }

    private async Task RefreshDynamicBiometricSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var interval = await _repository.fnGetBioInterval(cancellationToken);
            if (interval > 0)
            {
                _biometricOptions.PollingIntervalSeconds = interval;
                _logger.LogInformation("Loaded dynamic biometric polling interval {PollingIntervalSeconds} from configuration.", interval);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load dynamic biometric polling interval. Using configured value {PollingIntervalSeconds}.", _biometricOptions.PollingIntervalSeconds);
        }

        try
        {
            var lookBack = await _repository.fnGetBioLookBack(cancellationToken);
            if (lookBack > 0)
            {
                _biometricOptions.InitialLookBackMinutes = lookBack;
                _logger.LogInformation("Loaded dynamic biometric look-back period {InitialLookBackMinutes} from configuration.", lookBack);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load dynamic biometric look-back period. Using configured value {InitialLookBackMinutes}.", _biometricOptions.InitialLookBackMinutes);
        }
    }

    private Task LogBiometricEventAsync(int serverId, int error, string type, string message, CancellationToken cancellationToken)
    {
        return _repository.fnSaveBiometricLogs(serverId, error, type, message, cancellationToken);
    }
}

