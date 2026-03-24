using Microsoft.Extensions.Hosting;

namespace WorkerHost.Windows;

public static class WindowsServiceHostBuilder
{
    /// <summary>
    /// Configures the host to run as a Windows Service (SCM) or as a console app (when not installed as a service).
    /// ServiceName defaults to the entry-assembly name; pass a non-empty value to <paramref name="serviceName"/>
    /// to override it (e.g. read from configuration before calling this method).
    /// </summary>
    public static IHostBuilder ConfigureWindowsService(this IHostBuilder builder, string? serviceName = null)
    {
        return builder.UseWindowsService(options =>
        {
            if (!string.IsNullOrEmpty(serviceName))
                options.ServiceName = serviceName;
        });
    }
}
