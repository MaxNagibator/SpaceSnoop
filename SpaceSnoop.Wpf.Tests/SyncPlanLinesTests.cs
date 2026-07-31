using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncPlanLinesTests
{
    private const long Megabyte = 1024L * 1024L;

    [Test]
    public void План_называет_объём_рядом_с_количеством()
    {
        var planned = new PlannedActions(3, 2, 1, 1, 1)
        {
            NewCopyBytes = 3 * Megabyte,
            ModifiedCopyBytes = 2 * Megabyte,
            CopyToRightBytes = 5 * Megabyte,
            DeleteFileBytes = Megabyte,
            DeleteDirBytes = 4 * Megabyte,
        };

        var lines = SyncViewModel.BuildPlanLines(planned, null, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo("Скопировать файлов: 5 (5МБ)"));
            Assert.That(lines, Has.Some.EqualTo("    – новых: 3 (3МБ)"));
            Assert.That(lines, Has.Some.EqualTo("    – изменённых: 2 (2МБ)"));
            Assert.That(lines, Has.Some.EqualTo("Удалить файлов в корзину: 1 (1МБ)"));
            Assert.That(lines, Has.Some.EqualTo("Удалить каталогов в корзину: 1 (4МБ)"));
            Assert.That(lines.Any(x => x.Contains("место освободится после её очистки", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void Нехватка_места_на_приёмнике_предупреждает()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = 10 * Megabyte, CopyToRightBytes = 10 * Megabyte };
        var receivers = new PlanReceiver[] { new("D:\\Backup", 10 * Megabyte, 4 * Megabyte) };

        var lines = SyncViewModel.BuildPlanLines(planned, null, receivers);

        Assert.That(lines, Has.Some.EqualTo("Внимание: на D:\\Backup не хватает ≈6МБ – потребуется ≈10МБ, свободно 4МБ."));
    }

    [Test]
    public void Достаточное_место_докладывается_без_предупреждения()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = 10 * Megabyte, CopyToRightBytes = 10 * Megabyte };
        var receivers = new PlanReceiver[] { new("D:\\Backup", 10 * Megabyte, 40 * Megabyte) };

        var lines = SyncViewModel.BuildPlanLines(planned, null, receivers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo("Приёмник D:\\Backup: потребуется ≈10МБ, свободно 40МБ."));
            Assert.That(lines.Any(x => x.Contains("Внимание", StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public void Неизвестное_свободное_место_оговаривается()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = Megabyte, CopyToRightBytes = Megabyte };
        var receivers = new PlanReceiver[] { new("\\\\server\\share", Megabyte, null) };

        var lines = SyncViewModel.BuildPlanLines(planned, null, receivers);

        Assert.That(lines, Has.Some.EqualTo("Приёмник \\\\server\\share: потребуется ≈1МБ, свободное место неизвестно."));
    }

    [Test]
    public void Пустой_план_не_говорит_ни_о_месте_ни_о_корзине()
    {
        var lines = SyncViewModel.BuildPlanLines(PlannedActions.Empty, "слева направо", [new("D:\\Backup", Megabyte, 0)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo("Направление: слева направо."));
            Assert.That(lines, Has.Some.EqualTo("Изменений нет."));
            Assert.That(lines.Any(x => x.Contains("Приёмник", StringComparison.Ordinal)), Is.False);
            Assert.That(lines.Any(x => x.Contains("корзин", StringComparison.Ordinal)), Is.False);
        }
    }
}
