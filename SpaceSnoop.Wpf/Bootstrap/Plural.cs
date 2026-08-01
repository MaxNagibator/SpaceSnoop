namespace SpaceSnoop.Wpf.Bootstrap;

public static class Plural
{
    public static string Format(int count, string one, string few, string many)
    {
        return $"{count} {Word(count, one, few, many)}";
    }

    public static string Word(int count, string one, string few, string many)
    {
        var tail = Math.Abs(count) % 100;

        if (tail is >= 11 and <= 14)
        {
            return many;
        }

        return (tail % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }
}
