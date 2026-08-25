namespace SpaceSnoop.Wpf.ViewModels.Sync;

public enum SyncPlanFreshness
{
    None = 0,
    Fresh = 1,
    Applied = 2,
    PartiallyApplied = 3,
    Interrupted = 4,
}

internal readonly record struct SyncPlanFreshnessState(
    SyncPlanFreshness Freshness,
    int ErrorCount = 0,
    int MismatchCount = 0,
    bool Cancelled = false)
{
    public bool IsExecutable => Freshness == SyncPlanFreshness.Fresh;

    public string? RefusalMessage => Freshness switch
    {
        SyncPlanFreshness.Fresh => null,
        SyncPlanFreshness.Applied => "План уже исполнен – выполните сравнение заново.",
        SyncPlanFreshness.PartiallyApplied =>
            $"План исполнен частично (ошибок: {ErrorCount:N0}, расхождений проверки: {MismatchCount:N0}) – выполните сравнение заново.",
        SyncPlanFreshness.Interrupted => Cancelled
            ? "Прогон отменён, часть файлов могла быть перенесена – выполните сравнение заново."
            : "Прогон прерван ошибкой, часть файлов могла быть перенесена – выполните сравнение заново.",
        _ => "Сравнение ещё не выполнялось.",
    };

    public SyncPlanFreshnessState AfterComparison()
    {
        return new(SyncPlanFreshness.Fresh);
    }

    public SyncPlanFreshnessState AfterSync(SyncReport? report, bool cancelled)
    {
        if (report is null)
        {
            return new(SyncPlanFreshness.Interrupted, Cancelled: cancelled);
        }

        var errors = report.Errors.Count;
        var mismatches = report.Mismatches.Count;

        if (errors == 0 && mismatches == 0 && !cancelled)
        {
            return new(SyncPlanFreshness.Applied);
        }

        return new(SyncPlanFreshness.PartiallyApplied, errors, mismatches, cancelled);
    }
}

internal sealed class SyncPlanStaleException(string message) : InvalidOperationException(message);
