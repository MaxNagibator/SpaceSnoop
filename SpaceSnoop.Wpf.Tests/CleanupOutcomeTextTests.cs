using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Cleanup;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class CleanupOutcomeTextTests
{
    [TestCase(0, 0, false, false, "Завершено")]
    [TestCase(1, 0, false, false, "Завершено")]
    [TestCase(1, 1, true, false, "Завершено с пропусками")]
    [TestCase(0, 1, true, false, "Не удалось очистить")]
    [TestCase(1, 0, true, false, "Завершено с ошибками")]
    [TestCase(0, 1, true, true, "Отменено")]
    public void Итог_отчёта_различает_частичное_удаление_полный_отказ_и_отмену(int deleted, int skipped, bool error, bool cancelled, string expected)
    {
        IReadOnlyList<string> errors = error ? ["Причина"] : [];
        Assert.That(CleanupOutcomeText.For(new CleanupReport(0, deleted, skipped, errors, cancelled)), Is.EqualTo(expected));
    }

    [TestCase(DotNetCliOutcome.None, "Завершено с ошибкой")]
    [TestCase(DotNetCliOutcome.Succeeded, "Завершено")]
    [TestCase(DotNetCliOutcome.Failed, "Завершено с ошибкой")]
    [TestCase(DotNetCliOutcome.Cancelled, "Отменено")]
    [TestCase(DotNetCliOutcome.TimedOut, "Истекло время ожидания")]
    [TestCase(DotNetCliOutcome.LaunchFailed, "Не удалось выполнить")]
    [TestCase(DotNetCliOutcome.LoggingFailed, "Не удалось выполнить")]
    public void Незапущенная_команда_не_называется_завершённой(DotNetCliOutcome outcome, string expected)
    {
        Assert.That(CleanupOutcomeText.For(outcome), Is.EqualTo(expected));
    }
}
