using Microsoft.Extensions.Hosting;

namespace LeAPAttendanceService;

public static class ServiceDefaultsExtensions
{
    public static HostApplicationBuilder AddServiceDefaults(this HostApplicationBuilder builder)
    {
        // Placeholder for future shared defaults (logging, telemetry, health checks, etc.).
        // Keeping this extension avoids template drift when reusing code from the original HRBiometric service.
        return builder;
    }
}

