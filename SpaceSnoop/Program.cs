using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NLog.Windows.Forms;
using SpaceSnoop.Services;
using System.ComponentModel;
using LogLevel = NLog.LogLevel;

namespace SpaceSnoop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var logger = LogManager.GetCurrentClassLogger();

        try
        {
            using var servicesProvider = new ServiceCollection()
                .ConfigureServices()
                .AddLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    loggingBuilder.AddNLog(ConfigureLogging());
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
                .AddTransient<SortService>()
                .AddTransient<SpaceColorCalculator>()
                .AddTransient<DiskSpaceCalculator>()
                .AddSingleton<AdministratorChecker>()
            ;
    }

    private static LoggingConfiguration ConfigureLogging()
    {
        var logDirectory = Path.Combine(Path.GetTempPath(), "SpaceSnoop");
        Directory.CreateDirectory(logDirectory);
        logDirectory = logDirectory.Replace("\\", "/");

        var config = new LoggingConfiguration();

        var systemFile = new FileTarget("system")
        {
            FileName = "${" + logDirectory + "}/logs/${shortdate}/system.log",
        };

        config.AddTarget(systemFile);

        var customFile = new FileTarget("custom")
        {
            FileName = "${" + logDirectory + "}/logs/${shortdate}/${logger:shortName=True}.log",
        };

        config.AddTarget(customFile);

        var richTextBoxTarget = new RichTextBoxTarget
        {
            Name = "textBox",
            Layout = "${longdate}${newline}[${level:uppercase=true}]|${logger:shortName=True}|${newline}${message}${newline}",
            ControlName = "_uiLogsRichTextBox",
            FormName = "MainForm",
            Height = 600,
            Width = 400,
            AutoScroll = true,
            MaxLines = 0,
            ShowMinimized = false,
            ToolWindow = true,
            UseDefaultRowColoringRules = true,
            AllowAccessoryFormCreation = false,
            MessageRetention = RichTextBoxTargetMessageRetentionStrategy.All,
            SupportLinks = false,
        };

        config.AddTarget(richTextBoxTarget);

        config.LoggingRules.Add(new("*", LogLevel.Trace, richTextBoxTarget));

        var customRule = new LoggingRule("SpaceSnoop.*", LogLevel.Trace, customFile)
        {
            Final = true,
        };

        config.LoggingRules.Add(customRule);

        var systemRule = new LoggingRule("*", LogLevel.Info, systemFile);
        systemRule.EnableLoggingForLevels(LogLevel.Debug, LogLevel.Error);
        config.LoggingRules.Add(systemRule);

        return config;
    }
}
