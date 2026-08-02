namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncOutcomeNarrative
{
    internal static (string Title, string Message)? DescribeProblems(SyncReport report)
    {
        const int MaxShown = 20;

        if (report.Errors.Count > 0)
        {
            var list = string.Join(Environment.NewLine, report.Errors.Take(MaxShown).Select(e => $"  {e.RelativePath}: {e.Message}"));

            if (report.Errors.Count > MaxShown)
            {
                list += $"{Environment.NewLine}  …и ещё {report.Errors.Count - MaxShown}";
            }

            return ("Ошибки", $"Ошибки при синхронизации:{Environment.NewLine}{list}");
        }

        if (report.Mismatches.Count > 0)
        {
            var list = string.Join(Environment.NewLine, report.Mismatches.Take(MaxShown).Select(m => $"  {m.RelativePath}: {m.Reason}"));

            if (report.Mismatches.Count > MaxShown)
            {
                list += $"{Environment.NewLine}  …и ещё {report.Mismatches.Count - MaxShown}";
            }

            return ("Расхождения после синхронизации", $"После применения проверка нашла расхождения:{Environment.NewLine}{list}");
        }

        return null;
    }

    internal static (string Message, StatusSeverity Severity) DescribeToast(SyncReport report)
    {
        var toastVolume = report.CopiedBytes > 0 ? $" · {SizeFormatter.Format(report.CopiedBytes)}" : string.Empty;

        var message = report switch
        {
            { Errors.Count: > 0 } => $"Синхронизация: применено {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}",
            { Mismatches.Count: > 0 } => $"Синхронизация: применено {report.SuccessCount:N0} · расхождений: {report.Mismatches.Count:N0}",
            _ => $"Синхронизация завершена: применено {report.SuccessCount:N0}{toastVolume}",
        };

        var severity = report switch
        {
            { Errors.Count: > 0 } => StatusSeverity.Error,
            { Mismatches.Count: > 0 } => StatusSeverity.Warning,
            _ => StatusSeverity.Success,
        };

        return (message, severity);
    }
}
