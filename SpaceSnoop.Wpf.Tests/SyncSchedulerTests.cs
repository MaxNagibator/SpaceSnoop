using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Schedule;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncSchedulerTests
{
    private const string Exe = @"C:\Program Files\SpaceSnoop\SpaceSnoop.Wpf.exe";
    private const string Id = "ab12cd34";

    [Test]
    public void Команда_создания_содержит_имя_задачи_путь_и_аргумент_профиля()
    {
        var args = SyncScheduler.BuildCreateArgs(SyncScheduler.TaskNameFor(Id), ScheduleInterval.Daily, new(3, 0, 0), Exe, $"--sync {Id}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Value(args, "/tn"), Is.EqualTo($"SpaceSnoop Sync [{Id}]"));
            Assert.That(Value(args, "/tr"), Is.EqualTo($"\"{Exe}\" --sync {Id}"));
            Assert.That(args, Does.Contain("/f"));
        }
    }

    [Test]
    public void Пустой_аргумент_оставляет_только_путь_в_команде()
    {
        var args = SyncScheduler.BuildCreateArgs(SyncScheduler.TaskNameFor(Id), ScheduleInterval.Daily, new(3, 0, 0), Exe, string.Empty);

        Assert.That(Value(args, "/tr"), Is.EqualTo($"\"{Exe}\""));
    }

    [TestCase(ScheduleInterval.Daily, "DAILY", true)]
    [TestCase(ScheduleInterval.Hourly, "HOURLY", true)]
    [TestCase(ScheduleInterval.OnLogon, "ONLOGON", false)]
    public void Периодичность_задаёт_расписание_и_наличие_времени(ScheduleInterval interval, string schedule, bool hasTime)
    {
        var args = SyncScheduler.BuildCreateArgs(SyncScheduler.TaskNameFor(Id), interval, new(3, 30, 0), Exe, $"--sync {Id}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Value(args, "/sc"), Is.EqualTo(schedule));
            Assert.That(args.Contains("/st"), Is.EqualTo(hasTime));

            if (hasTime)
            {
                Assert.That(Value(args, "/st"), Is.EqualTo("03:30"));
            }
        }
    }

    [TestCase(0, SyncMode.LeftToRight)]
    [TestCase(1, SyncMode.RightToLeft)]
    [TestCase(2, SyncMode.Bidirectional)]
    [TestCase(99, SyncMode.LeftToRight)]
    public void Индекс_режима_отображается_в_направление(int index, SyncMode expected)
    {
        Assert.That(HeadlessSync.MapMode(index), Is.EqualTo(expected));
    }

    [Test]
    public void Статус_парсится_из_verbose_CSV_строки()
    {
        const string csv =
            @"""DESK"",""\SpaceSnoop Sync"",""24.06.2026 3:00:00"",""Ready"",""Interactive"",""23.06.2026 3:00:05"",""0"",""DESK\u"",""task""";

        var status = ScheduleStatus.Parse(csv);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Exists, Is.True);
            Assert.That(status.NextRun, Is.EqualTo("24.06.2026 3:00:00"));
            Assert.That(status.LastRun, Is.EqualTo("23.06.2026 3:00:05"));
            Assert.That(status.LastResult, Is.EqualTo(0));
            Assert.That(status.LastResultText, Is.EqualTo("Успех"));
            Assert.That(status.Action, Is.EqualTo("task"));
        }
    }

    [TestCase(@"""C:\New\app.exe"" --sync ab12cd34", @"C:\New\app.exe", false)]
    [TestCase(@"""C:\new\APP.exe"" --sync ab12cd34", @"C:\New\app.exe", false)]
    [TestCase(@"""C:\Old\app.exe"" --sync ab12cd34", @"C:\New\app.exe", true)]
    [TestCase("", @"C:\New\app.exe", false)]
    [TestCase(@"""C:\Old\app.exe""", "", false)]
    public void Устаревший_путь_задачи_определяется_по_подстроке(string action, string exe, bool stale)
    {
        Assert.That(SyncScheduler.IsStale(action, exe), Is.EqualTo(stale));
    }

    [Test]
    public void Пустой_вывод_даёт_отсутствующий_статус()
    {
        Assert.That(ScheduleStatus.Parse(string.Empty).Exists, Is.False);
    }

    [TestCase(0, "Успех")]
    [TestCase(1, "Завершилась с ошибками")]
    [TestCase(3, "Каталог недоступен")]
    [TestCase(5, "Каталоги пересекаются")]
    [TestCase(6, "Проверка нашла расхождения")]
    [TestCase(ScheduleStatus.NeverRun, "Ещё не запускалась")]
    [TestCase(ScheduleStatus.Running, "Выполняется")]
    [TestCase(12345, "Код 12345")]
    public void Код_результата_расшифровывается(int code, string expected)
    {
        Assert.That(ScheduleStatus.DecodeResult(code), Is.EqualTo(expected));
    }

    [Test]
    public void Отключённая_задача_читается_из_xml()
    {
        const string xml = "<Task><Settings><Enabled>false</Enabled></Settings><Triggers /></Task>";

        Assert.That(ScheduleStatus.ParseEnabled(xml), Is.False);
    }

    [Test]
    public void Включённый_триггер_не_путается_с_состоянием_задачи()
    {
        const string xml = "<Task><Triggers><CalendarTrigger><Enabled>false</Enabled></CalendarTrigger></Triggers><Settings><Enabled>true</Enabled></Settings></Task>";

        Assert.That(ScheduleStatus.ParseEnabled(xml), Is.True);
    }

    [TestCase("<Task><Settings /></Task>")]
    [TestCase("")]
    public void Без_явного_состояния_задача_считается_включённой(string xml)
    {
        Assert.That(ScheduleStatus.ParseEnabled(xml), Is.True);
    }

    [TestCase(@"C:\A", @"C:\B", false)]
    [TestCase(@"C:\A", @"C:\AB", false)]
    [TestCase(@"C:\A", @"C:\A", true)]
    [TestCase(@"C:\A\", @"C:\A", true)]
    [TestCase(@"C:\A", @"C:\A\Sub", true)]
    [TestCase(@"C:\A\Sub", @"C:\A", true)]
    public void Совпадающие_и_вложенные_каталоги_распознаются(string left, string right, bool overlap)
    {
        Assert.That(SyncProfile.PathsOverlap(left, right), Is.EqualTo(overlap));
    }

    private static string Value(List<string> args, string flag)
    {
        return args[args.IndexOf(flag) + 1];
    }
}
