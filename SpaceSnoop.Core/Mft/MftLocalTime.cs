namespace SpaceSnoop.Core.Mft;

internal sealed class MftLocalTime
{
    private const int Slots = 1 << 14;
    private const int CodeBits = 20;
    private const long CodeMask = (1L << CodeBits) - 1;
    private const long ShiftBias = 1L << (CodeBits - 1);
    private const long ExactCode = CodeMask;
    private const long ProbeTicks = 6 * TimeSpan.TicksPerHour;
    private const long MarginTicks = TimeSpan.TicksPerDay;

    private static readonly long FileTimeEpoch = DateTime.FromFileTimeUtc(0).Ticks;
    private static readonly long LastDay = (DateTime.MaxValue.Ticks - FileTimeEpoch) / TimeSpan.TicksPerDay;

    private readonly Func<TimeZoneInfo>? _currentZone;
    private readonly Func<long, DateTime> _exact;
    private Cache _cache;

    public static MftLocalTime Local { get; } = new(
        () => TimeZoneInfo.Local,
        x => DateTime.FromFileTimeUtc(x).ToLocalTime());

    public TimeZoneInfo Zone => Volatile.Read(ref _cache).TimeZone;

    public MftLocalTime(TimeZoneInfo zone, Func<long, DateTime> exact)
    {
        _exact = exact;
        _cache = new(zone);
    }

    public MftLocalTime(Func<TimeZoneInfo> currentZone, Func<long, DateTime> exact)
    {
        _currentZone = currentZone;
        _exact = exact;
        _cache = new(currentZone());
    }

    public DateTime FromFileTime(long fileTime)
    {
        if (fileTime < 0)
        {
            return _exact(fileTime);
        }

        var cache = GetCache();
        var day = fileTime / TimeSpan.TicksPerDay;
        ref var slot = ref cache.DaySlots[(int)(day & (Slots - 1))];
        var packed = Volatile.Read(ref slot);

        if (packed >> CodeBits != day + 1)
        {
            if (!TryMeasure(cache, day, out packed))
            {
                return _exact(fileTime);
            }

            Volatile.Write(ref slot, packed);
        }

        var code = packed & CodeMask;

        return code == ExactCode
            ? _exact(fileTime)
            : new(FileTimeEpoch + fileTime + ((code - ShiftBias) * TimeSpan.TicksPerSecond), DateTimeKind.Local);
    }

    private Cache GetCache()
    {
        var cache = Volatile.Read(ref _cache);

        if (_currentZone is null)
        {
            return cache;
        }

        var zone = _currentZone();

        if (ReferenceEquals(cache.TimeZone, zone))
        {
            return cache;
        }

        var next = new Cache(zone);

        if (!ReferenceEquals(_currentZone(), zone))
        {
            return next;
        }

        var prior = Interlocked.CompareExchange(ref _cache, next, cache);

        if (ReferenceEquals(prior, cache))
        {
            return next;
        }

        return ReferenceEquals(prior.TimeZone, zone) ? prior : next;
    }

    private bool TryMeasure(Cache cache, long day, out long packed)
    {
        if (_currentZone is not null && !ReferenceEquals(cache.TimeZone, _currentZone()))
        {
            packed = 0;
            return false;
        }

        if (day < 3 || day > (LastDay - 3) || IsTransitionDay(cache.Rules, day))
        {
            packed = Pack(day, ExactCode);
            return true;
        }

        var from = (day * TimeSpan.TicksPerDay) - MarginTicks;
        var to = ((day + 1) * TimeSpan.TicksPerDay) + MarginTicks;

        long? shift = null;

        for (var probe = from; probe < (to + ProbeTicks); probe += ProbeTicks)
        {
            var at = Math.Min(probe, to - 1);
            var measured = _exact(at).Ticks - FileTimeEpoch - at;

            if (_currentZone is not null
                && (!ReferenceEquals(cache.TimeZone, _currentZone())
                    || measured != cache.TimeZone.GetUtcOffset(DateTime.FromFileTimeUtc(at)).Ticks))
            {
                packed = 0;
                return false;
            }

            if (shift is not null && measured != shift)
            {
                packed = Pack(day, ExactCode);
                return true;
            }

            shift = measured;
        }

        if (_currentZone is not null && !ReferenceEquals(cache.TimeZone, _currentZone()))
        {
            packed = 0;
            return false;
        }

        var seconds = shift!.Value / TimeSpan.TicksPerSecond;

        if (shift.Value % TimeSpan.TicksPerSecond != 0 || Math.Abs(seconds) >= (ShiftBias - 1))
        {
            packed = Pack(day, ExactCode);
            return true;
        }

        packed = Pack(day, seconds + ShiftBias);
        return true;
    }

    private static bool IsTransitionDay(TimeZoneInfo.AdjustmentRule[] rules, long day)
    {
        if (rules.Length == 0)
        {
            return false;
        }

        var windowStart = FileTimeEpoch + ((day - 2) * TimeSpan.TicksPerDay);
        var windowEnd = FileTimeEpoch + ((day + 3) * TimeSpan.TicksPerDay) - 1;
        var fromYear = new DateTime(windowStart, DateTimeKind.Utc).Year;
        var toYear = new DateTime(windowEnd, DateTimeKind.Utc).Year;

        foreach (var rule in rules)
        {
            if (windowEnd < rule.DateStart.Ticks || windowStart > rule.DateEnd.Ticks)
            {
                continue;
            }

            if (IsNearDate(day, rule.DateStart) || IsNearDate(day, rule.DateEnd))
            {
                return true;
            }

            if (rule.DaylightDelta == TimeSpan.Zero)
            {
                continue;
            }

            for (var year = fromYear; year <= toYear; year++)
            {
                if (year < rule.DateStart.Year || year > rule.DateEnd.Year)
                {
                    continue;
                }

                if (IsNearTransition(day, year, rule.DaylightTransitionStart)
                    || IsNearTransition(day, year, rule.DaylightTransitionEnd))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsNearDate(long day, DateTime date)
    {
        if (date.Ticks <= FileTimeEpoch || date.Ticks >= (DateTime.MaxValue.Ticks - TimeSpan.TicksPerDay))
        {
            return false;
        }

        var boundaryDay = (date.Ticks - FileTimeEpoch) / TimeSpan.TicksPerDay;
        return Math.Abs(day - boundaryDay) <= 2;
    }

    private static bool IsNearTransition(long day, int year, TimeZoneInfo.TransitionTime transition)
    {
        if (transition.Month is < 1 or > 12)
        {
            return true;
        }

        if (transition.Month == 1
            && transition.Day == 1
            && transition.TimeOfDay.Ticks < TimeSpan.TicksPerSecond
            && (IsNearDate(day, new(year, 1, 1)) || IsNearDate(day, new(year, 12, 31))))
        {
            return true;
        }

        var dayOfMonth = transition.IsFixedDateRule
            ? Math.Min(transition.Day, DateTime.DaysInMonth(year, transition.Month))
            : FloatingDayOfMonth(year, transition.Month, transition.Week, transition.DayOfWeek);

        var transitionDay = (new DateTime(year, transition.Month, dayOfMonth).Ticks - FileTimeEpoch) / TimeSpan.TicksPerDay;
        return Math.Abs(day - transitionDay) <= 2;
    }

    private static int FloatingDayOfMonth(int year, int month, int week, DayOfWeek dayOfWeek)
    {
        if (week == 5)
        {
            var lastDay = DateTime.DaysInMonth(year, month);
            var last = new DateTime(year, month, lastDay);
            var back = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
            return lastDay - back;
        }

        var first = new DateTime(year, month, 1);
        var forward = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return 1 + forward + ((week - 1) * 7);
    }

    private static long Pack(long day, long code)
    {
        return ((day + 1) << CodeBits) | code;
    }

    private sealed class Cache(TimeZoneInfo zone)
    {
        public TimeZoneInfo TimeZone { get; } = zone;

        public TimeZoneInfo.AdjustmentRule[] Rules { get; } = zone.GetAdjustmentRules();

        public long[] DaySlots { get; } = new long[Slots];
    }
}
