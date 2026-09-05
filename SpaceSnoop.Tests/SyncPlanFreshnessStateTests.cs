using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncPlanFreshnessStateTests
{
    [Test]
    public void Default_IsNotExecutableAndNamesMissingComparison()
    {
        var state = default(SyncPlanFreshnessState);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(SyncPlanFreshness.None));
            Assert.That(state.IsExecutable, Is.False);
            Assert.That(state.RefusalMessage, Does.Contain("Сравнение ещё не выполнялось"));
        }
    }

    [Test]
    public void AfterComparison_IsExecutableWithoutRefusal()
    {
        var state = default(SyncPlanFreshnessState).AfterComparison();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(SyncPlanFreshness.Fresh));
            Assert.That(state.IsExecutable, Is.True);
            Assert.That(state.RefusalMessage, Is.Null);
        }
    }

    [Test]
    public void AfterSync_CleanReport_BlocksRepeatRun()
    {
        var state = default(SyncPlanFreshnessState).AfterComparison().AfterSync(new(), false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(state.IsExecutable, Is.False);
            Assert.That(state.RefusalMessage, Does.Contain("уже исполнен"));
        }
    }

    [TestCase(1, 0, false, TestName = "AfterSync_ReportWithErrors_IsPartial")]
    [TestCase(0, 1, false, TestName = "AfterSync_ReportWithMismatches_IsPartial")]
    [TestCase(0, 0, true, TestName = "AfterSync_CancelledWithReport_IsPartial")]
    public void AfterSync_IncompleteRun_IsPartiallyApplied(int errors, int mismatches, bool cancelled)
    {
        var report = new SyncReport();

        for (var i = 0; i < errors; i++)
        {
            report.Errors.Add(new($"error-{i}.txt", SyncAction.CopyToRight, "нет доступа"));
        }

        for (var i = 0; i < mismatches; i++)
        {
            report.Mismatches.Add(new($"mismatch-{i}.txt", SyncAction.CopyToRight, "размер не сошёлся"));
        }

        var state = default(SyncPlanFreshnessState).AfterComparison().AfterSync(report, cancelled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(SyncPlanFreshness.PartiallyApplied));
            Assert.That(state.IsExecutable, Is.False);
            Assert.That(state.ErrorCount, Is.EqualTo(errors));
            Assert.That(state.MismatchCount, Is.EqualTo(mismatches));
            Assert.That(state.RefusalMessage, Does.Contain("исполнен частично"));
        }
    }

    [TestCase(true, "отменён", TestName = "AfterSync_CancelledWithoutReport_NamesCancellation")]
    [TestCase(false, "прерван ошибкой", TestName = "AfterSync_FailedWithoutReport_NamesFailure")]
    public void AfterSync_NoReport_IsInterrupted(bool cancelled, string expected)
    {
        var state = default(SyncPlanFreshnessState).AfterComparison().AfterSync(null, cancelled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Freshness, Is.EqualTo(SyncPlanFreshness.Interrupted));
            Assert.That(state.IsExecutable, Is.False);
            Assert.That(state.RefusalMessage, Does.Contain(expected));
        }
    }

    [Test]
    public void AfterComparison_ReturnsExecutabilityToStalePlan()
    {
        var stale = default(SyncPlanFreshnessState).AfterComparison().AfterSync(new(), false);
        var fresh = stale.AfterComparison();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stale.IsExecutable, Is.False);
            Assert.That(fresh.IsExecutable, Is.True);
            Assert.That(fresh.ErrorCount, Is.Zero);
            Assert.That(fresh.MismatchCount, Is.Zero);
        }
    }
}
