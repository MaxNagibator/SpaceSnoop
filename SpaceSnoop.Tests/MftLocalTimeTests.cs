using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftLocalTimeTests
{
    private static readonly long Step = TimeSpan.FromMinutes(7).Ticks;

    [Test]
    public void Вставленная_секунда_сдвигает_метки_после_себя_как_точный_пересчёт()
    {
        var leap = new DateTime(2024, 6, 30, 23, 59, 59, DateTimeKind.Utc).ToFileTimeUtc();
        var epoch = DateTime.FromFileTimeUtc(0).Ticks;
        Func<long, DateTime> exact = fileTime =>
            new(epoch + fileTime + TimeSpan.FromHours(3).Ticks - (fileTime > leap ? TimeSpan.TicksPerSecond : 0), DateTimeKind.Local);

        var (transitions, mismatches) = Sweep(
            new(TimeZoneInfo.Utc, exact),
            exact,
            leap - TimeSpan.FromDays(10).Ticks,
            leap + TimeSpan.FromDays(10).Ticks,
            (fast, precise) => fast.Ticks == precise.Ticks);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitions, Is.EqualTo(1));
            Assert.That(mismatches, Is.Empty);
        }
    }

    [Test]
    public void Метка_в_поясе_машины_совпадает_с_ToLocalTime_вместе_с_признаком_неоднозначности()
    {
        var (_, mismatches) = Sweep(
            MftLocalTime.Local,
            fileTime => DateTime.FromFileTimeUtc(fileTime).ToLocalTime(),
            new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            (fast, precise) => fast.Ticks == precise.Ticks
                && fast.Kind == precise.Kind
                && fast.ToUniversalTime().Ticks == precise.ToUniversalTime().Ticks);

        Assert.That(mismatches, Is.Empty);
    }

    [TestCase("Central European Standard Time", 2023, 2025, TestName = "Переходы CET совпадают с точным пересчётом")]
    [TestCase("Lord Howe Standard Time", 2023, 2025, TestName = "Получасовые переходы Лорд-Хау совпадают с точным пересчётом")]
    [TestCase("Chatham Islands Standard Time", 2023, 2025, TestName = "Переходы Чатема со смещением 12:45 совпадают с точным пересчётом")]
    [TestCase("Morocco Standard Time", 2019, 2021, TestName = "Отмена летнего времени Марокко на рамадан совпадает с точным пересчётом")]
    [TestCase("Russian Standard Time", 2010, 2015, TestName = "Отмена и смена поясного времени Москвы совпадают с точным пересчётом")]
    [TestCase("Tomsk Standard Time", 2013, 2015, TestName = "Стык годовых правил Томска совпадает с точным пересчётом")]
    [TestCase("North Korea Standard Time", 2014, 2016, TestName = "Получасовой стык правил Пхеньяна совпадает с точным пересчётом")]
    public void Метка_у_перехода_совпадает_с_точным_пересчётом(string zoneId, int fromYear, int toYear)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        Func<long, DateTime> exact = fileTime => TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(fileTime), zone);

        var (transitions, mismatches) = Sweep(
            new(zone, exact),
            exact,
            new DateTime(fromYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            new DateTime(toYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            (fast, precise) => fast.Ticks == precise.Ticks);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitions, Is.GreaterThan(0));
            Assert.That(mismatches, Is.Empty);
        }
    }

    [Test]
    public void Границы_правил_всех_системных_поясов_совпадают_с_точным_пересчётом()
    {
        var failures = new List<string>();
        var checkedBoundaries = 0;

        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var boundaryDays = new HashSet<long>();

            foreach (var rule in zone.GetAdjustmentRules())
            {
                AddBoundaryDay(boundaryDays, rule.DateStart);
                AddBoundaryDay(boundaryDays, rule.DateEnd);
            }

            if (boundaryDays.Count == 0)
            {
                continue;
            }

            Func<long, DateTime> exact = x => TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), zone);
            var clock = new MftLocalTime(zone, exact);

            foreach (var day in boundaryDays)
            {
                checkedBoundaries++;

                var (_, mismatches) = Sweep(
                    clock,
                    exact,
                    (day - 1) * TimeSpan.TicksPerDay,
                    (day + 2) * TimeSpan.TicksPerDay,
                    (fast, precise) => fast.Ticks == precise.Ticks);

                if (mismatches.Count > 0)
                {
                    var first = DateTime.FromFileTimeUtc(mismatches[0]);
                    failures.Add($"{zone.Id} @ {first:yyyy-MM-dd HH:mm:ss}Z ({mismatches.Count})");
                    break;
                }
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(checkedBoundaries, Is.GreaterThan(0));
            Assert.That(failures, Is.Empty);
        }
    }

    [TestCase("Tomsk Standard Time", 2011, 12, 31, 16, 37, TestName = "Стык 31 декабря Томска без смены смещения идёт через точный пересчёт")]
    [TestCase("Yukon Standard Time", 2021, 1, 1, 6, 37, TestName = "Стык 1 января Юкона без смены смещения идёт через точный пересчёт")]
    public void Стык_правил_без_смены_смещения_идёт_через_точный_пересчёт(
        string zoneId,
        int year,
        int month,
        int day,
        int hour,
        int minute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var target = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var exactCalled = false;
        Func<long, DateTime> exact = x =>
        {
            if (x == target)
            {
                exactCalled = true;
            }

            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), zone);
        };

        var clock = new MftLocalTime(zone, exact);
        _ = clock.FromFileTime(target);
        exactCalled = false;

        var local = clock.FromFileTime(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exactCalled, Is.True);
            Assert.That(local.Ticks, Is.EqualTo(exact(target).Ticks));
        }
    }

    private static void AddBoundaryDay(HashSet<long> days, DateTime date)
    {
        if (date.Year is < 1602 or > 9998)
        {
            return;
        }

        var utc = new DateTime(date.Ticks, DateTimeKind.Utc);
        days.Add(utc.ToFileTimeUtc() / TimeSpan.TicksPerDay);
    }

    private static (int Transitions, List<long> Mismatches) Sweep(
        MftLocalTime clock,
        Func<long, DateTime> exact,
        long from,
        long to,
        Func<DateTime, DateTime, bool> same)
    {
        var transitions = 0;
        var mismatches = new List<long>();
        var previous = from;
        var previousShift = Shift(previous);

        for (var fileTime = from; fileTime < to; fileTime += Step)
        {
            Check(fileTime);

            var shift = Shift(fileTime);

            if (shift != previousShift)
            {
                transitions++;

                var boundary = Boundary(previous, fileTime, previousShift);

                Check(boundary - 1);
                Check(boundary);
                Check(boundary + 1);
            }

            previous = fileTime;
            previousShift = shift;
        }

        return (transitions, mismatches);

        long Shift(long fileTime)
        {
            return exact(fileTime).Ticks - fileTime;
        }

        long Boundary(long low, long high, long lowShift)
        {
            while ((high - low) > 1)
            {
                var middle = low + ((high - low) / 2);

                if (Shift(middle) == lowShift)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return high;
        }

        void Check(long fileTime)
        {
            if (!same(clock.FromFileTime(fileTime), exact(fileTime)))
            {
                mismatches.Add(fileTime);
            }
        }
    }
}
