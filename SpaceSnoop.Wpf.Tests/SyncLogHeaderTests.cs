using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncLogHeaderTests
{
    private static readonly DateTime Stamp = new(2026, 8, 4, 19, 32, 5);

    [TestCase(SyncLogOrigin.Scheduled, "Ночной бэкап", "[2026-08-04 19:32:05] Автосинхронизация [Ночной бэкап]: 7 успешно, 0 ошибок")]
    [TestCase(SyncLogOrigin.Overview, "Ночной бэкап", "[2026-08-04 19:32:05] Обзор [Ночной бэкап]: 7 успешно, 0 ошибок")]
    [TestCase(SyncLogOrigin.Manual, null, "[2026-08-04 19:32:05] Синхронизация: 7 успешно, 0 ошибок")]
    [TestCase(SyncLogOrigin.Mcp, null, "[2026-08-04 19:32:05] Синхронизация (запуск агентом через MCP): 7 успешно, 0 ошибок")]
    public void Заголовок_сохраняет_прежний_формат_каждого_запуска(SyncLogOrigin origin, string? name, string expected)
    {
        var header = SyncLog.FormatHeader(origin, name, Report(7, 0), SyncVerifyState.None, Stamp);

        Assert.That(header, Is.EqualTo(expected));
    }

    [TestCase(SyncLogOrigin.Scheduled)]
    [TestCase(SyncLogOrigin.Overview)]
    [TestCase(SyncLogOrigin.Manual)]
    [TestCase(SyncLogOrigin.Mcp)]
    public void Записанный_запуск_узнаётся_читателем_по_своему_origin(SyncLogOrigin origin)
    {
        var header = SyncLog.FormatHeader(origin, "Профиль", Report(1, 0), SyncVerifyState.None, Stamp);

        Assert.That(SyncLog.MatchesOrigin(header, origin), Is.True);
    }

    [TestCase(SyncLogOrigin.Overview)]
    [TestCase(SyncLogOrigin.Manual)]
    [TestCase(SyncLogOrigin.Mcp)]
    public void История_расписания_не_подхватывает_чужие_запуски(SyncLogOrigin origin)
    {
        var header = SyncLog.FormatHeader(origin, "Профиль", Report(1, 0), SyncVerifyState.None, Stamp);

        Assert.That(SyncLog.MatchesOrigin(header, SyncLogOrigin.Scheduled), Is.False);
    }

    [Test]
    public void Ручной_запуск_и_запуск_агентом_не_путаются_между_собой()
    {
        var manual = SyncLog.FormatHeader(SyncLogOrigin.Manual, null, Report(1, 0), SyncVerifyState.None, Stamp);
        var mcp = SyncLog.FormatHeader(SyncLogOrigin.Mcp, null, Report(1, 0), SyncVerifyState.None, Stamp);

        Assert.Multiple(() =>
        {
            Assert.That(SyncLog.MatchesOrigin(mcp, SyncLogOrigin.Manual), Is.False);
            Assert.That(SyncLog.MatchesOrigin(manual, SyncLogOrigin.Mcp), Is.False);
        });
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(20, true)]
    [TestCase(100, true)]
    public void Записанный_заголовок_подсвечивается_ровно_при_реальных_ошибках(int errors, bool expected)
    {
        var header = SyncLog.FormatHeader(SyncLogOrigin.Scheduled, "Профиль", Report(5, errors), SyncVerifyState.None, Stamp);

        Assert.That(SyncLog.LineHasErrors(header), Is.EqualTo(expected));
    }

    [TestCase(SyncVerifyState.None, 0, "[2026-08-04 19:32:05] Автосинхронизация [Профиль]: 5 успешно, 0 ошибок")]
    [TestCase(SyncVerifyState.Completed, 0, "[2026-08-04 19:32:05] Автосинхронизация [Профиль]: 5 успешно, 0 ошибок, расхождений: 0")]
    [TestCase(SyncVerifyState.Completed, 3, "[2026-08-04 19:32:05] Автосинхронизация [Профиль]: 5 успешно, 0 ошибок, расхождений: 3")]
    [TestCase(SyncVerifyState.Interrupted, 0, "[2026-08-04 19:32:05] Автосинхронизация [Профиль]: 5 успешно, 0 ошибок, проверка прервана (расхождений к тому моменту: 0)")]
    public void Исход_проверки_дописывается_в_конец_заголовка(SyncVerifyState verify, int mismatches, string expected)
    {
        var header = SyncLog.FormatHeader(SyncLogOrigin.Scheduled, "Профиль", Report(5, 0, mismatches), verify, Stamp);

        Assert.That(header, Is.EqualTo(expected));
    }

    [TestCase(SyncVerifyState.None, 0, 0, false)]
    [TestCase(SyncVerifyState.Completed, 0, 0, false)]
    [TestCase(SyncVerifyState.Completed, 0, 3, true)]
    [TestCase(SyncVerifyState.Completed, 2, 0, true)]
    [TestCase(SyncVerifyState.Interrupted, 0, 0, true)]
    public void Расхождения_и_прерванная_проверка_подсвечиваются_наравне_с_ошибками(SyncVerifyState verify, int errors, int mismatches, bool expected)
    {
        var header = SyncLog.FormatHeader(SyncLogOrigin.Scheduled, "Профиль", Report(5, errors, mismatches), verify, Stamp);

        Assert.That(SyncLog.LineHasErrors(header), Is.EqualTo(expected));
    }

    [TestCase("резерв, расхождений: 0")]
    [TestCase("резерв: 5 успешно, 0 ошибок")]
    public void Имя_профиля_не_управляет_подсветкой(string name)
    {
        var clean = SyncLog.FormatHeader(SyncLogOrigin.Scheduled, name, Report(5, 0), SyncVerifyState.None, Stamp);
        var failed = SyncLog.FormatHeader(SyncLogOrigin.Scheduled, name, Report(5, 3), SyncVerifyState.None, Stamp);

        Assert.Multiple(() =>
        {
            Assert.That(SyncLog.LineHasErrors(clean), Is.False);
            Assert.That(SyncLog.LineHasErrors(failed), Is.True);
        });
    }

    [Test]
    public void Строка_чужого_формата_не_считается_запуском()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SyncLog.MatchesOrigin("Автосинхронизация [Профиль]: 1 успешно, 0 ошибок", SyncLogOrigin.Scheduled), Is.False);
            Assert.That(SyncLog.MatchesOrigin("  файл.txt", SyncLogOrigin.Scheduled), Is.False);
            Assert.That(SyncLog.MatchesOrigin(SyncLog.FormatHeader(SyncLogOrigin.Scheduled, "Профиль", Report(1, 0), SyncVerifyState.None, Stamp), SyncLogOrigin.None), Is.False);
        });
    }

    private static SyncReport Report(int success, int errors, int mismatches = 0)
    {
        var report = new SyncReport { CopiedCount = success };

        for (var i = 0; i < errors; i++)
        {
            report.Errors.Add(new($@"файл{i}.txt", SyncAction.CopyToRight, "нет доступа"));
        }

        for (var i = 0; i < mismatches; i++)
        {
            report.Mismatches.Add(new($@"файл{i}.txt", SyncAction.CopyToRight, "приёмник не найден"));
        }

        return report;
    }
}
