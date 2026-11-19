namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

internal static class CleanupOutcomeText
{
    public static string For(CleanupReport report)
    {
        if (report.Cancelled)
        {
            return "Отменено";
        }

        if (report.Deleted == 0 && (report.Skipped > 0 || report.Errors.Count > 0))
        {
            return "Не удалось очистить";
        }

        if (report.Skipped > 0)
        {
            return "Завершено с пропусками";
        }

        return report.Errors.Count > 0 ? "Завершено с ошибками" : "Завершено";
    }

    public static string For(DotNetCliOutcome outcome)
    {
        return outcome switch
        {
            DotNetCliOutcome.Cancelled => "Отменено",
            DotNetCliOutcome.TimedOut => "Истекло время ожидания",
            DotNetCliOutcome.Succeeded => "Завершено",
            DotNetCliOutcome.LaunchFailed or DotNetCliOutcome.LoggingFailed => "Не удалось выполнить",
            _ => "Завершено с ошибкой",
        };
    }
}
