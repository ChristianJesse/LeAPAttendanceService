using System.Data;
using Dapper;
using LeAPAttendanceService.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace LeAPAttendanceService.Services;

public sealed class LeAPRepository : ILeAPRepository
{
    private readonly ILogger<LeAPRepository> _logger;
    private readonly DatabaseOptions _options;
    private readonly IConfigSqlLoader _sqlLoader;
    private readonly string _connectionString;

    public LeAPRepository(
        ILogger<LeAPRepository> logger,
        IOptions<DatabaseOptions> options,
        IConfigSqlLoader sqlLoader)
    {
        _logger = logger;
        _options = options.Value;
        _sqlLoader = sqlLoader;

        _connectionString = !string.IsNullOrWhiteSpace(_options.ConnectionString)
            ? _options.ConnectionString
            : _sqlLoader.LoadSqlConfiguration();

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException($"Database connection string could not be resolved. Configure {DatabaseOptions.SectionName}:ConnectionString or the SqlRegistry settings.");
        }
    }

    public async Task<IReadOnlyList<LeapBiometricServer>> fnGetBiometricServersAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("@pOption", 38, DbType.Int32);

        var servers = await connection.QueryAsync<LeapBiometricServer>(
            "spLEAP",
            parameters,
            commandType: CommandType.StoredProcedure);

        return servers.ToList();
    }

    public async Task fnSaveAttendanceRecordsAsync(int serverId, IEnumerable<BiometricAttendanceRecord> records, CancellationToken cancellationToken)
    {
        var table = CreateAttendanceTable(serverId, records);
        var rowCount = table.Rows.Count;

        _logger.LogInformation("Saving {RowCount} attendance rows for ServerID={ServerID} with TVP type {TypeName}.", rowCount, serverId, _options.AttendanceRecordsTypeName);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("spLEAP", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.Add(new SqlParameter("@pOption", SqlDbType.Int) { Value = 3 });
        command.Parameters.Add(new SqlParameter("@pTypeLEAPInOutRecords", SqlDbType.Structured)
        {
            TypeName = _options.AttendanceRecordsTypeName,
            Value = table
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> fnGetBioInterval(CancellationToken cancellationToken)
    {
        return await GetConfigurationValueAsync(39, "BioInterval", cancellationToken);
    }

    public async Task<int> fnGetBioLookBack(CancellationToken cancellationToken)
    {
        return await GetConfigurationValueAsync(39, "BioLookBack", cancellationToken);
    }

    public async Task fnSaveBiometricLogs(int serverId, int error, string type, string message, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("spLEAP", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.Add(new SqlParameter("@pOption", SqlDbType.Int) { Value = 40 });
        command.Parameters.Add(new SqlParameter("@pServerID", SqlDbType.Int) { Value = serverId });
        command.Parameters.Add(new SqlParameter("@pError", SqlDbType.Int) { Value = error });
        command.Parameters.Add(new SqlParameter("@pType", SqlDbType.NVarChar, 128) { Value = type });
        command.Parameters.Add(new SqlParameter("@pMessage", SqlDbType.NVarChar, -1) { Value = message ?? string.Empty });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetConfigurationValueAsync(int option, string code, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("@pOption", option, DbType.Int32);
        parameters.Add("@pCode", code, DbType.String);

        var rawValue = await connection.QuerySingleAsync<object>(
            "spLEAP",
            parameters,
            commandType: CommandType.StoredProcedure);

        return rawValue switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            decimal decimalValue => (int)decimalValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Configured value for '{code}' is not a valid integer.")
        };
    }

    private static DataTable CreateAttendanceTable(int serverId, IEnumerable<BiometricAttendanceRecord> records)
    {
        var table = new DataTable();
        table.Columns.Add("ServerID", typeof(int));
        table.Columns.Add("IDNumber", typeof(string));
        table.Columns.Add("InOutStatus", typeof(string));
        table.Columns.Add("TimeInOut", typeof(DateTime));

        foreach (var record in records)
        {
            table.Rows.Add(serverId, record.EmployeeId, record.InOutStatus, record.Timestamp);
        }

        return table;
    }
}
