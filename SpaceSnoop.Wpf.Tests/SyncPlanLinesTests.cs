using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
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

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Скопировать файлов", "5", "5 МБ")));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("новых", "3", "3 МБ", ConfirmMetricTone.Sub)));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("изменённых", "2", "2 МБ", ConfirmMetricTone.Sub)));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Удалить файлов в корзину", "1", "1 МБ", ConfirmMetricTone.Danger)));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Удалить каталогов целиком", "1", "4 МБ", ConfirmMetricTone.Danger)));
            Assert.That(Text(lines), Does.Contain("место освободится после её очистки"));
        }
    }

    [Test]
    public void Удаляемые_каталоги_оговаривают_своё_содержимое()
    {
        var planned = new PlannedActions(0, 0, 2, 0, 1) { DeleteFileBytes = Megabyte, DeleteDirBytes = 4 * Megabyte };

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, []);

        Assert.That(Text(lines), Does.Contain("эти файлы в число 2 не входят"));
    }

    [Test]
    public void Удаление_каталогов_без_файлов_не_ссылается_на_счётчик()
    {
        var planned = new PlannedActions(0, 0, 0, 0, 1) { DeleteDirBytes = 4 * Megabyte };

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Text(lines), Does.Contain("Каталог уходит в корзину со всем содержимым."));
            Assert.That(Text(lines), Does.Not.Contain("не входят"));
        }
    }

    [Test]
    public void Нехватка_места_на_приёмнике_предупреждает()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = 10 * Megabyte, CopyToRightBytes = 10 * Megabyte };
        var receivers = new PlanReceiver[] { new("D:\\Backup", 10 * Megabyte, 4 * Megabyte) };

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, receivers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Свободно", string.Empty, "4 МБ", ConfirmMetricTone.Danger)));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmTextLine("Не хватает ≈6 МБ.", ConfirmTextTone.Danger)));
        }
    }

    [Test]
    public void Достаточное_место_докладывается_без_предупреждения()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = 10 * Megabyte, CopyToRightBytes = 10 * Megabyte };
        var receivers = new PlanReceiver[] { new("D:\\Backup", 10 * Megabyte, 40 * Megabyte) };

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, receivers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo(new ConfirmTextLine("Приёмник D:\\Backup", ConfirmTextTone.Muted)));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Потребуется", string.Empty, "≈10 МБ")));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Свободно", string.Empty, "40 МБ")));
            Assert.That(Text(lines), Does.Not.Contain("Не хватает"));
        }
    }

    [Test]
    public void Неизвестное_свободное_место_оговаривается()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = Megabyte, CopyToRightBytes = Megabyte };
        var receivers = new PlanReceiver[] { new("\\\\server\\share", Megabyte, null) };

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, receivers);

        Assert.That(lines, Has.Some.EqualTo(new ConfirmMetricLine("Свободно", string.Empty, "неизвестно")));
    }

    [Test]
    public void Односторонний_приёмник_в_двустороннем_режиме_оговаривается()
    {
        var planned = new PlannedActions(1, 0, 0, 0, 0) { NewCopyBytes = Megabyte, CopyToRightBytes = Megabyte };
        var receivers = new PlanReceiver[] { new("D:\\Backup", Megabyte, 40 * Megabyte) };

        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, receivers, bothWays: true);

        Assert.That(Text(lines), Does.Contain("Во встречном направлении копирования нет."));
    }

    [Test]
    public void Пустой_план_не_говорит_ни_о_месте_ни_о_корзине()
    {
        var lines = SyncPlanNarrative.BuildPlanLines(PlannedActions.Empty, "слева направо", [new("D:\\Backup", Megabyte, 0)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Has.Some.EqualTo(new ConfirmTextLine("Направление: слева направо.")));
            Assert.That(lines, Has.Some.EqualTo(new ConfirmTextLine("Изменений нет.")));
            Assert.That(Text(lines), Does.Not.Contain("Приёмник"));
            Assert.That(Text(lines), Does.Not.Contain("корзин"));
        }
    }

    private static string Text(IEnumerable<ConfirmLine> lines)
    {
        return ConfirmDialogViewModel.AsText(lines);
    }
}
