using Microsoft.Extensions.Logging;
using Serilog;

namespace Administrador_Desarrollo_Web.Services;

public static class LoggingSetup
{
    public static ILoggerFactory Configure(string logsPath)
    {
        Directory.CreateDirectory(logsPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logsPath, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: true));
    }
}
