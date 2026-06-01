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

    public async Task<IReadOnlyList<LeapBiometricServer>> GetBiometricServersAsync(CancellationToken cancellationToken)
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

    public async Task SaveAttendanceRecordsAsync(int serverId, IEnumerable<BiometricAttendanceRecord> records, CancellationToken cancellationToken)
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
