using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncSchedulerTests
{
    private const string Exe = @"C:\Program Files\SpaceSnoop\SpaceSnoop.Wpf.exe";

    [Test]
    public void Команда_создания_содержит_имя_задачи_путь_и_флаг_синхронизации()
    {
        var args = SyncScheduler.BuildCreateArgs(ScheduleInterval.Daily, new(3, 0, 0), Exe);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Value(args, "/tn"), Is.EqualTo(SyncScheduler.TaskName));
            Assert.That(Value(args, "/tr"), Is.EqualTo($"\"{Exe}\" --sync"));
            Assert.That(args, Does.Contain("/f"));
        }
    }

    [TestCase(ScheduleInterval.Daily, "DAILY", true)]
    [TestCase(ScheduleInterval.Hourly, "HOURLY", true)]
    [TestCase(ScheduleInterval.OnLogon, "ONLOGON", false)]
    public void Периодичность_задаёт_расписание_и_наличие_времени(ScheduleInterval interval, string schedule, bool hasTime)
    {
        var args = SyncScheduler.BuildCreateArgs(interval, new(3, 30, 0), Exe);

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

    private static string Value(List<string> args, string flag)
    {
        return args[args.IndexOf(flag) + 1];
    }
}
