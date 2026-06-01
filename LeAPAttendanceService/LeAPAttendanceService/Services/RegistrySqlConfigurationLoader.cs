using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace LeAPAttendanceService.Services;

public sealed class RegistrySqlConfigurationLoader : IConfigSqlLoader
{
    private readonly ILogger<RegistrySqlConfigurationLoader> _logger;
    private readonly SqlRegistryOptions _options;
    private readonly IConfigConnection _configConnection;

    public RegistrySqlConfigurationLoader(
        ILogger<RegistrySqlConfigurationLoader> logger,
        IOptions<SqlRegistryOptions> options,
        IConfigConnection configConnection)
    {
        _logger = logger;
        _options = options.Value;
        _configConnection = configConnection;
    }

    public string LoadSqlConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.Module))
        {
            throw new InvalidOperationException($"SqlRegistry module name is not configured. Set {SqlRegistryOptions.SectionName}:Module in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_options.FolderName))
        {
            throw new InvalidOperationException($"SqlRegistry folder name is not configured. Set {SqlRegistryOptions.SectionName}:FolderName in appsettings.json.");
        }

        var hkey = $"HKEY_LOCAL_MACHINE\\SOFTWARE\\{_options.FolderName}\\{_options.Module}";
        var sqlServer = Registry.GetValue(hkey, "Server", null) as string;
        var sqlDb = Registry.GetValue(hkey, "Database", null) as string;

#if DEBUG
        // Override values in debug builds if you need to target a different environment.
        // Uncomment or replace these values as appropriate for local debugging.
        //sqlServer = "ndessdev\\ndessdev";
        //sqlDb = "NDESS_DEV02";
#endif

        if (string.IsNullOrWhiteSpace(sqlServer) || string.IsNullOrWhiteSpace(sqlDb))
        {
            throw new InvalidOperationException(
                $"Registry values for SQL connection were not found. Expected Server and Database under {hkey}.");
        }

        var sqlConnStr = $"Server={sqlServer};Database={sqlDb};Integrated Security=True;Trusted_Connection=Yes;TrustServerCertificate=True;";

        _logger.LogInformation("Connection string for SQL Server: {SqlServer}, Database: {SqlDb}", sqlServer, sqlDb);

        using var conn = new SqlConnection(sqlConnStr);
        conn.Open();
        _logger.LogInformation("Database connection successful. Server={SqlServer}; Database={SqlDb}", sqlServer, sqlDb);

        _configConnection.Initialize(string.Empty, sqlConnStr);
        return sqlConnStr;
    }
}
