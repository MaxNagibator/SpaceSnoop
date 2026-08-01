namespace SpaceSnoop.Core.Docker;

public static class DockerText
{
    private const int ShortIdLength = 12;
    private const int AnonymousVolumeLength = 64;

    private static readonly string[] Seconds = ["секунду", "секунды", "секунд"];
    private static readonly string[] Minutes = ["минуту", "минуты", "минут"];
    private static readonly string[] Hours = ["час", "часа", "часов"];
    private static readonly string[] Days = ["день", "дня", "дней"];
    private static readonly string[] Weeks = ["неделю", "недели", "недель"];
    private static readonly string[] Months = ["месяц", "месяца", "месяцев"];
    private static readonly string[] Years = ["год", "года", "лет"];

    public static string Category(string type)
    {
        return type.Trim() switch
        {
            "Images" => "Образы",
            "Containers" => "Контейнеры",
            "Local Volumes" => "Тома",
            "Build Cache" => "Кэш сборки",
            _ => type,
        };
    }

    public static string Age(string createdSince)
    {
        var value = createdSince.Trim();

        if (value.Length == 0)
        {
            return string.Empty;
        }

        var duration = Duration(StripAgo(value));

        return duration is null ? value : $"{duration} назад";
    }

    public static string Status(string status)
    {
        var value = status.Trim();

        if (value.Length == 0)
        {
            return string.Empty;
        }

        return value switch
        {
            "Created" => "Создан",
            "Dead" => "Не отвечает",
            "Paused" => "На паузе",
            "Removal In Progress" => "Удаляется",
            _ => Running(value) ?? Stopped(value) ?? value,
        };
    }

    public static string ShortName(string name)
    {
        var value = name.Trim();

        return value.Length >= AnonymousVolumeLength && IsHex(value) ? value[..ShortIdLength] : value;
    }

    private static string? Running(string value)
    {
        if (!value.StartsWith("Up", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = value[2..].Trim();

        if (rest.Length == 0)
        {
            return "Работает";
        }

        var note = string.Empty;
        var open = rest.LastIndexOf('(');

        if (open >= 0 && rest.EndsWith(')'))
        {
            note = Health(rest[(open + 1)..^1]);
            rest = rest[..open].Trim();
        }

        var duration = Duration(rest);
        var head = duration is null ? "Работает" : $"Работает {duration}";

        return note.Length == 0 ? head : $"{head}, {note}";
    }

    private static string? Stopped(string value)
    {
        string prefix;
        string verb;

        if (value.StartsWith("Exited", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "Exited";
            verb = "Остановлен";
        }
        else if (value.StartsWith("Restarting", StringComparison.OrdinalIgnoreCase))
        {
            prefix = "Restarting";
            verb = "Перезапускается";
        }
        else
        {
            return null;
        }

        var rest = value[prefix.Length..].Trim();
        var code = string.Empty;

        if (rest.StartsWith('('))
        {
            var close = rest.IndexOf(')');

            if (close < 0)
            {
                return null;
            }

            code = rest[1..close].Trim();
            rest = rest[(close + 1)..].Trim();
        }

        var duration = Duration(StripAgo(rest));
        var head = code.Length == 0 ? verb : $"{verb} ({code})";

        return duration is null ? head : $"{head}, {duration} назад";
    }

    private static string Health(string note)
    {
        return note.Trim().ToLowerInvariant() switch
        {
            "healthy" => "здоров",
            "unhealthy" => "нездоров",
            "health: starting" => "проверяется",
            "paused" => "на паузе",
            _ => note.Trim(),
        };
    }

    private static string? Duration(string text)
    {
        var value = text.Trim();

        if (value.Length == 0)
        {
            return null;
        }

        if (value.Equals("Less than a second", StringComparison.OrdinalIgnoreCase))
        {
            return "меньше секунды";
        }

        if (value.Equals("About a minute", StringComparison.OrdinalIgnoreCase))
        {
            return "около минуты";
        }

        if (value.Equals("About an hour", StringComparison.OrdinalIgnoreCase))
        {
            return "около часа";
        }

        var space = value.IndexOf(' ');

        if (space <= 0 || !int.TryParse(value[..space], out var count))
        {
            return null;
        }

        var forms = Forms(value[(space + 1)..].Trim().TrimEnd('s'));

        return forms is null ? null : $"{count} {Plural(count, forms)}";
    }

    private static string[]? Forms(string unit)
    {
        return unit.ToLowerInvariant() switch
        {
            "second" => Seconds,
            "minute" => Minutes,
            "hour" => Hours,
            "day" => Days,
            "week" => Weeks,
            "month" => Months,
            "year" => Years,
            _ => null,
        };
    }

    private static string Plural(int count, string[] forms)
    {
        if ((count % 100) is >= 11 and <= 14)
        {
            return forms[2];
        }

        return (count % 10) switch
        {
            1 => forms[0],
            2 or 3 or 4 => forms[1],
            _ => forms[2],
        };
    }

    private static string StripAgo(string value)
    {
        return value.EndsWith(" ago", StringComparison.OrdinalIgnoreCase) ? value[..^4].Trim() : value;
    }

    private static bool IsHex(string value)
    {
        foreach (var symbol in value)
        {
            if (!char.IsAsciiHexDigit(symbol))
            {
                return false;
            }
        }

        return true;
    }
}
