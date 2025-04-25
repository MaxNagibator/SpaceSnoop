using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Extensions.Logging;
using NLog.Windows.Forms;
using SpaceSnoop.Services;
using System.ComponentModel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace SpaceSnoop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var logger = LogManager.GetCurrentClassLogger();

        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            using var servicesProvider = new ServiceCollection()
                .ConfigureServices()
                .AddLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                    loggingBuilder.AddNLog(config);
                })
                .BuildServiceProvider();

            var administratorChecker = servicesProvider.GetRequiredService<AdministratorChecker>();

            if (administratorChecker.IsRestartRequired())
            {
                return;
            }

            ApplicationConfiguration.Initialize();

            var form = servicesProvider.GetRequiredService<MainForm>();
            RichTextBoxTarget.ReInitializeAllTextboxes(form);

            Application.Run(form);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Программа остановлена из-за исключения");
            throw;
        }
        finally
        {
            LogManager.Shutdown();
        }
    }

    private static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        return services
                .AddSingleton<MainForm>()
                .AddTransient<BackgroundWorker>()
                .AddTransient<ColorService>()
                .AddTransient<WorkerService>()
                .AddTransient<SpaceColorCalculator>()
                .AddTransient<DiskSpaceCalculator>()
                .AddSingleton<AdministratorChecker>()
            ;
    }
}
