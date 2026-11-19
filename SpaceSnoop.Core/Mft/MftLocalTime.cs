namespace SpaceSnoop.Core.Mft;

internal sealed class MftLocalTime(TimeZoneInfo zone, Func<long, DateTime> exact)
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

    private static MftLocalTime? _local;

    private readonly long[] _slots = new long[Slots];
    private readonly HashSet<long>? _exactDays = BuildExactDays(zone);

    public TimeZoneInfo Zone { get; } = zone;

    public static MftLocalTime Local
    {
        get
        {
            var zone = TimeZoneInfo.Local;
            var current = Volatile.Read(ref _local);

            if (current is not null && ReferenceEquals(current.Zone, zone))
            {
                return current;
            }

            current = new(zone, fileTime => DateTime.FromFileTimeUtc(fileTime).ToLocalTime());
            Volatile.Write(ref _local, current);
            return current;
        }
    }

    public DateTime FromFileTime(long fileTime)
    {
        if (fileTime < 0)
        {
            return exact(fileTime);
        }

        var day = fileTime / TimeSpan.TicksPerDay;
        ref var slot = ref _slots[(int)(day & (Slots - 1))];
        var packed = Volatile.Read(ref slot);

        if (packed >> CodeBits != day + 1)
        {
            packed = Measure(day);
            Volatile.Write(ref slot, packed);
        }

        var code = packed & CodeMask;

        return code == ExactCode
            ? exact(fileTime)
            : new(FileTimeEpoch + fileTime + ((code - ShiftBias) * TimeSpan.TicksPerSecond), DateTimeKind.Local);
    }

    private long Measure(long day)
    {
        if (day < 3 || day > (LastDay - 3) || (_exactDays is not null && _exactDays.Contains(day)))
        {
            return Pack(day, ExactCode);
        }

        var from = (day * TimeSpan.TicksPerDay) - MarginTicks;
        var to = ((day + 1) * TimeSpan.TicksPerDay) + MarginTicks;

        long? shift = null;

        for (var probe = from; probe < (to + ProbeTicks); probe += ProbeTicks)
        {
            var at = Math.Min(probe, to - 1);
            var measured = exact(at).Ticks - FileTimeEpoch - at;

            if (shift is not null && measured != shift)
            {
                return Pack(day, ExactCode);
            }

            shift = measured;
        }

        var seconds = shift!.Value / TimeSpan.TicksPerSecond;

        if (shift.Value % TimeSpan.TicksPerSecond != 0 || Math.Abs(seconds) >= (ShiftBias - 1))
        {
            return Pack(day, ExactCode);
        }

        return Pack(day, seconds + ShiftBias);
    }

    private static void AddBoundaryDays(HashSet<long> days, DateTime date)
    {
        if (date.Ticks <= FileTimeEpoch || date.Ticks >= (DateTime.MaxValue.Ticks - TimeSpan.TicksPerDay))
        {
            return;
        }

        var day = (date.Ticks - FileTimeEpoch) / TimeSpan.TicksPerDay;
        days.Add(day - 1);
        days.Add(day);
        days.Add(day + 1);
    }

    private static HashSet<long>? BuildExactDays(TimeZoneInfo zone)
    {
        var rules = zone.GetAdjustmentRules();

        if (rules.Length == 0)
        {
            return null;
        }

        var days = new HashSet<long>();

        foreach (var rule in rules)
        {
            AddBoundaryDays(days, rule.DateStart);
            AddBoundaryDays(days, rule.DateEnd);
        }

        return days.Count == 0 ? null : days;
    }

    private static long Pack(long day, long code)
    {
        return ((day + 1) << CodeBits) | code;
    }
}
