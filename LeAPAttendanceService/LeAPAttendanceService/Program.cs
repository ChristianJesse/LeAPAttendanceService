namespace LeAPAttendanceService;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            // This is the name that will appear in the Windows Services console.
            options.ServiceName = "LeAPAttendanceService";
        });

        builder.AddServiceDefaults();
        builder.Services.Configure<BiometricOptions>(builder.Configuration.GetSection(BiometricOptions.SectionName));
        builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
        builder.Services.Configure<SqlRegistryOptions>(builder.Configuration.GetSection(SqlRegistryOptions.SectionName));
        builder.Services.AddSingleton<IConfigConnection, ConfigConnection>();
        builder.Services.AddSingleton<IConfigSqlLoader, RegistrySqlConfigurationLoader>();
        builder.Services.AddSingleton<IBiometricService, ZkemkeeperBiometricService>();
        builder.Services.AddSingleton<ILeAPRepository, LeAPRepository>();
        builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        host.Run();
    }
}

