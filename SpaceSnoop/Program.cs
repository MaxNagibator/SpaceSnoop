using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Extensions.Logging;
using NLog.Windows.Forms;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace SpaceSnoop;

internal static class Program
{
    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Logger? logger = LogManager.GetCurrentClassLogger();

        try
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            using ServiceProvider servicesProvider = new ServiceCollection()
                .ConfigureServices()
                .AddLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                    loggingBuilder.AddNLog(config);
                })
                .BuildServiceProvider();

            IAdministratorChecker administratorChecker = servicesProvider.GetRequiredService<IAdministratorChecker>();

            if (administratorChecker.IsRestartRequired())
            {
                return;
            }

            ApplicationConfiguration.Initialize();

            MainForm form = servicesProvider.GetRequiredService<MainForm>();
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
                .AddTransient<IDiskSpaceCalculator, DiskSpaceCalculator>()
                .AddSingleton<IAdministratorChecker, AdministratorChecker>()
            ;
    }
}
