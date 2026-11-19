using System.Runtime.CompilerServices;
using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftLocalTimeTests
{
    private const ulong LocalKind = 2UL << 62;
    private const ulong AmbiguousLocalKind = 3UL << 62;

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
        var exact = ExactFor(zone);

        var (transitions, mismatches) = Sweep(
            new(zone, exact),
            exact,
            new DateTime(fromYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            new DateTime(toYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            SameDateTime);

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

            Func<long, DateTime> exact = x =>
                WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), zone), ambiguous: true);
            var clock = new MftLocalTime(zone, exact);

            foreach (var day in boundaryDays)
            {
                checkedBoundaries++;

                var (_, mismatches) = Sweep(
                    clock,
                    exact,
                    (day - 1) * TimeSpan.TicksPerDay,
                    (day + 2) * TimeSpan.TicksPerDay,
                    SameDateTime);

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

            return WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), zone), ambiguous: true);
        };

        var clock = new MftLocalTime(zone, exact);
        var expected = exact(target);
        _ = clock.FromFileTime(target);
        exactCalled = false;

        var local = clock.FromFileTime(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exactCalled, Is.True);
            Assert.That(local.Kind, Is.EqualTo(DateTimeKind.Local));
            Assert.That(SameDateTime(local, expected), Is.True);
        }
    }

    [Test]
    public void Короткое_окно_летнего_времени_внутри_правила_совпадает_с_точным_пересчётом()
    {
        var zones = new[]
        {
            CreateShortDstZone(
                "ShortFixed",
                TimeSpan.Zero,
                TimeSpan.FromMinutes(30),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 1, 0, 0), 6, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 2, 0, 0), 6, 1)),
            CreateShortDstZone(
                "ShortFloating",
                TimeSpan.FromHours(-5),
                TimeSpan.FromMinutes(30),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 2, 0, 0), 6, 1, DayOfWeek.Saturday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 3, 0, 0), 6, 1, DayOfWeek.Saturday)),
            CreateShortDstZone(
                "ShortNegativeDelta",
                TimeSpan.FromHours(2),
                TimeSpan.FromMinutes(-30),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 3, 0, 0), 6, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 4, 0, 0), 6, 1)),
            CreateShortDstZone(
                "Extreme14HourDelta",
                TimeSpan.FromHours(14),
                TimeSpan.FromHours(-14),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 0, 0, 0), 6, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 1, 0, 0), 6, 1)),
            CreateShortDstZone(
                "ShortLastWeek",
                TimeSpan.Zero,
                TimeSpan.FromMinutes(30),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 1, 0, 0), 5, 5, DayOfWeek.Friday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 2, 0, 0), 5, 5, DayOfWeek.Friday)),
        };

        var from = new DateTime(2024, 5, 29, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var to = new DateTime(2024, 6, 3, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var step = TimeSpan.FromMinutes(5).Ticks;
        var failures = new List<string>();

        foreach (var zone in zones)
        {
            var exact = ExactFor(zone);
            var clock = new MftLocalTime(zone, exact);

            for (var fileTime = from; fileTime < to; fileTime += step)
            {
                var actual = clock.FromFileTime(fileTime);
                var expected = exact(fileTime);

                if (!SameDateTime(actual, expected))
                {
                    failures.Add($"{zone.Id} @ {DateTime.FromFileTimeUtc(fileTime):yyyy-MM-dd HH:mm:ss}Z");
                    break;
                }
            }
        }

        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void Удержанный_Local_обновляет_кэш_после_сброса_кэша_поясов()
    {
        var clock = MftLocalTime.Local;
        var target = new DateTime(2024, 6, 1, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc();
        _ = clock.FromFileTime(target);
        var beforeZone = clock.Zone;

        TimeZoneInfo.ClearCachedData();

        var beforeLookupZone = clock.Zone;
        var after = clock.FromFileTime(target);
        var afterLookupZone = clock.Zone;
        var expected = DateTime.FromFileTimeUtc(target).ToLocalTime();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReferenceEquals(beforeLookupZone, beforeZone), Is.True);
            Assert.That(ReferenceEquals(afterLookupZone, TimeZoneInfo.Local), Is.True);
            Assert.That(ReferenceEquals(afterLookupZone, beforeZone), Is.False);
            Assert.That(SameDateTime(after, expected), Is.True);
        }
    }

    [Test]
    public void Смена_пояса_инвалидирует_прогретые_слоты_и_не_отравляет_кэш_при_замере()
    {
        var plus14 = TimeZoneInfo.CreateCustomTimeZone("Plus14", TimeSpan.FromHours(14), "Plus14", "Plus14");
        var minus14 = TimeZoneInfo.CreateCustomTimeZone("Minus14", TimeSpan.FromHours(-14), "Minus14", "Minus14");
        var active = plus14;
        var switchOnMeasure = false;
        Func<long, DateTime> exact = x =>
        {
            if (switchOnMeasure)
            {
                switchOnMeasure = false;
                active = minus14;
            }

            return WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), active), ambiguous: false);
        };

        var clock = new MftLocalTime(() => active, exact);
        var firstTarget = new DateTime(2024, 6, 1, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var warmed = clock.FromFileTime(firstTarget);

        active = minus14;
        var refreshed = clock.FromFileTime(firstTarget);

        active = plus14;
        _ = clock.FromFileTime(firstTarget);
        switchOnMeasure = true;
        var secondTarget = new DateTime(2024, 6, 20, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var duringSwitch = clock.FromFileTime(secondTarget);
        var afterSwitch = clock.FromFileTime(secondTarget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warmed, Is.EqualTo(new DateTime(2024, 6, 1, 15, 15, 0, DateTimeKind.Local)));
            Assert.That(refreshed, Is.EqualTo(new DateTime(2024, 5, 31, 11, 15, 0, DateTimeKind.Local)));
            Assert.That(duringSwitch, Is.EqualTo(new DateTime(2024, 6, 19, 11, 15, 0, DateTimeKind.Local)));
            Assert.That(afterSwitch, Is.EqualTo(new DateTime(2024, 6, 19, 11, 15, 0, DateTimeKind.Local)));
        }
    }

    [TestCase(0, 1, TestName = "Годовые маркеры 0 и 1 мс сохраняют признак неоднозначности")]
    [TestCase(500, 500, TestName = "Годовые маркеры 500 мс сохраняют признак неоднозначности")]
    [TestCase(999, 999, TestName = "Годовые маркеры 999 мс сохраняют признак неоднозначности")]
    public void Годовые_маркеры_плавающих_правил_сохраняют_признак_неоднозначности(int startMillisecond, int endMillisecond)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(-14),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 0, 0, 0, startMillisecond), 1, 1, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 0, 0, 0, endMillisecond), 1, 5, DayOfWeek.Monday));
        var zone = TimeZoneInfo.CreateCustomTimeZone("YearMarkerFloat", TimeSpan.FromHours(14), "YM", "YM", "YM", [rule]);
        var target = new DateTime(2023, 12, 31, 10, 5, 0, DateTimeKind.Utc).ToFileTimeUtc();
        Func<long, DateTime> exact = x =>
        {
            var converted = TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), zone);
            return WithLocalKind(converted, ambiguous: x == target);
        };

        var clock = new MftLocalTime(zone, exact);
        var expected = exact(target);
        var cold = clock.FromFileTime(target);
        var warm = clock.FromFileTime(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SameDateTime(cold, expected), Is.True);
            Assert.That(SameDateTime(warm, expected), Is.True);
        }
    }

    [Test]
    public void Фиксированное_правило_29_февраля_работает_в_високосный_и_невисокосный_год()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new(2023, 1, 1),
            new(2025, 12, 31),
            TimeSpan.FromMinutes(30),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 1, 0, 0), 2, 29),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 2, 0, 0), 2, 29));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Feb29", TimeSpan.Zero, "Feb29", "Feb29", "Feb29", [rule]);
        var exact = ExactFor(zone);
        var clock = new MftLocalTime(zone, exact);

        var targets = new[]
        {
            new DateTime(2023, 2, 28, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            new DateTime(2024, 2, 28, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc(),
            new DateTime(2024, 2, 29, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc(),
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var target in targets)
            {
                Assert.That(SameDateTime(clock.FromFileTime(target), exact(target)), Is.True);
            }
        }
    }

    [Test]
    public void Возврат_исходного_пояса_во_время_замера_не_отравляет_кэш()
    {
        var plus14 = TimeZoneInfo.CreateCustomTimeZone("Plus14", TimeSpan.FromHours(14), "Plus14", "Plus14");
        var minus14 = TimeZoneInfo.CreateCustomTimeZone("Minus14", TimeSpan.FromHours(-14), "Minus14", "Minus14");
        var active = plus14;
        var probes = 0;
        var armAba = false;
        var singleProbeMismatch = false;
        Func<long, DateTime> exact = x =>
        {
            if (singleProbeMismatch)
            {
                return WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), minus14), ambiguous: false);
            }

            if (armAba)
            {
                probes++;
                if (probes == 1)
                {
                    active = minus14;
                }
            }

            var result = WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), active), ambiguous: false);
            if (armAba && probes == 13)
            {
                active = plus14;
                armAba = false;
            }

            return result;
        };

        var clock = new MftLocalTime(() => active, exact);
        var firstTarget = new DateTime(2024, 6, 20, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc();
        armAba = true;
        _ = clock.FromFileTime(firstTarget);
        var afterAba = clock.FromFileTime(firstTarget);

        var secondTarget = new DateTime(2024, 7, 10, 1, 15, 0, DateTimeKind.Utc).ToFileTimeUtc();
        singleProbeMismatch = true;
        _ = clock.FromFileTime(secondTarget);
        singleProbeMismatch = false;
        var afterSingleProbe = clock.FromFileTime(secondTarget);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterAba, Is.EqualTo(new DateTime(2024, 6, 20, 15, 15, 0, DateTimeKind.Local)));
            Assert.That(afterSingleProbe, Is.EqualTo(new DateTime(2024, 7, 10, 15, 15, 0, DateTimeKind.Local)));
        }
    }

    [Test]
    public void Запоздалая_публикация_кэша_не_затирает_более_новый_прогретый_пояс()
    {
        var zoneA = TimeZoneInfo.CreateCustomTimeZone("ZoneA", TimeSpan.Zero, "A", "A");
        var zoneB = TimeZoneInfo.CreateCustomTimeZone("ZoneB", TimeSpan.FromHours(2), "B", "B");
        var zoneC = TimeZoneInfo.CreateCustomTimeZone("ZoneC", TimeSpan.FromHours(5), "C", "C");
        var active = zoneA;
        var exactCalls = 0;
        var target = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        MftLocalTime clock = null!;
        var zoneBLookups = 0;
        Func<TimeZoneInfo> currentZone = () =>
        {
            var picked = active;
            if (ReferenceEquals(picked, zoneB))
            {
                zoneBLookups++;
                if (zoneBLookups == 2)
                {
                    active = zoneC;
                    _ = clock.FromFileTime(target);
                    exactCalls = 0;
                }
            }

            return picked;
        };

        Func<long, DateTime> exact = x =>
        {
            exactCalls++;
            return WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), active), ambiguous: false);
        };

        clock = new(currentZone, exact);
        active = zoneB;
        _ = clock.FromFileTime(target);
        exactCalls = 0;

        var after = clock.FromFileTime(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.EqualTo(new DateTime(2024, 6, 15, 17, 0, 0, DateTimeKind.Local)));
            Assert.That(exactCalls, Is.Zero);
        }
    }

    [Test]
    public void Неактивное_правило_с_пустым_переходом_не_мешает_пересчёту_вне_его_диапазона()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new(2024, 6, 1),
            new(2024, 6, 30),
            TimeSpan.FromMinutes(30),
            default,
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1, 1, 1, 2, 0, 0), 6, 15));
        var zone = TimeZoneInfo.CreateCustomTimeZone("InactiveDefault", TimeSpan.Zero, "ID", "ID", "ID", [rule]);
        var exactCalls = 0;
        Func<long, DateTime> exact = x =>
        {
            exactCalls++;
            return WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(x), zone), ambiguous: false);
        };

        var clock = new MftLocalTime(zone, exact);
        var target = new DateTime(2024, 2, 10, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

        var first = clock.FromFileTime(target);
        exactCalls = 0;
        var warm = clock.FromFileTime(target);
        var expected = WithLocalKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.FromFileTimeUtc(target), zone), ambiguous: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SameDateTime(first, expected), Is.True);
            Assert.That(SameDateTime(warm, expected), Is.True);
            Assert.That(exactCalls, Is.Zero);
        }
    }

    [Test]
    public void Повторяющийся_час_различает_летнее_и_стандартное_время()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var exact = ExactFor(zone);
        var clock = new MftLocalTime(zone, exact);
        var first = new DateTime(2024, 11, 3, 5, 30, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var second = new DateTime(2024, 11, 3, 6, 30, 0, DateTimeKind.Utc).ToFileTimeUtc();

        var dst = clock.FromFileTime(first);
        var standard = clock.FromFileTime(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dst.Ticks, Is.EqualTo(standard.Ticks));
            Assert.That(Unsafe.BitCast<DateTime, ulong>(dst) >> 62, Is.EqualTo(3UL));
            Assert.That(Unsafe.BitCast<DateTime, ulong>(standard) >> 62, Is.EqualTo(2UL));
            Assert.That(SameDateTime(dst, exact(first)), Is.True);
            Assert.That(SameDateTime(standard, exact(second)), Is.True);
        }
    }

    private static TimeZoneInfo CreateShortDstZone(
        string id,
        TimeSpan baseOffset,
        TimeSpan delta,
        TimeZoneInfo.TransitionTime start,
        TimeZoneInfo.TransitionTime end)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new(2024, 1, 1),
            new(2024, 12, 31),
            delta,
            start,
            end);
        return TimeZoneInfo.CreateCustomTimeZone(id, baseOffset, id, id, id, [rule]);
    }

    private static bool SameDateTime(DateTime left, DateTime right)
    {
        return Unsafe.BitCast<DateTime, ulong>(left) == Unsafe.BitCast<DateTime, ulong>(right);
    }

    private static DateTime WithLocalKind(DateTime value, bool ambiguous)
    {
        return Unsafe.BitCast<ulong, DateTime>((ulong)value.Ticks | (ambiguous ? AmbiguousLocalKind : LocalKind));
    }

    private static Func<long, DateTime> ExactFor(TimeZoneInfo zone)
    {
        return x =>
        {
            var utc = DateTime.FromFileTimeUtc(x);
            var converted = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            return WithLocalKind(converted, zone.IsAmbiguousTime(utc) && zone.IsDaylightSavingTime(utc));
        };
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
