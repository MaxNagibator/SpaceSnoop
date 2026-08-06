using System.Globalization;
using System.Text;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed record ScheduleStatus
{
    public const int NeverRun = 267011;
    public const int Running = 267009;

    public static ScheduleStatus Missing { get; } = new();

    public bool Exists { get; init; }
    public bool Enabled { get; init; } = true;
    public string NextRun { get; init; } = "–";
    public string LastRun { get; init; } = "–";
    public int LastResult { get; init; }
    public string LastResultText { get; init; } = "–";
    public string Action { get; init; } = string.Empty;

    public static ScheduleStatus Parse(string csv)
    {
        var line = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(static row => row.Contains('"'));

        if (line is null)
        {
            return Missing;
        }

        var fields = SplitCsv(line);

        if (fields.Count < 7)
        {
            return Missing;
        }

        var hasResult = int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result);

        return new()
        {
            Exists = true,
            NextRun = Clean(fields[2]),
            LastRun = Clean(fields[5]),
            LastResult = result,
            LastResultText = hasResult ? DecodeResult(result) : Clean(fields[6]),
            Action = fields.Count >= 9 ? fields[8].Trim() : string.Empty,
        };
    }

    public static string DecodeResult(int code)
    {
        return code switch
        {
            0 => "Успех",
            1 => "Завершилась с ошибками",
            2 => "Каталоги не настроены",
            3 => "Каталог недоступен",
            4 => "Зеркало отменено: источник пуст",
            5 => "Каталоги пересекаются",
            6 => "Проверка нашла расхождения",
            NeverRun => "Ещё не запускалась",
            Running => "Выполняется",
            _ => $"Код {code}",
        };
    }

    public static bool ParseEnabled(string xml)
    {
        var start = xml.IndexOf("<Settings>", StringComparison.Ordinal);
        var end = xml.IndexOf("</Settings>", StringComparison.Ordinal);

        if (start < 0 || end <= start)
        {
            return true;
        }

        return xml.IndexOf("<Enabled>false</Enabled>", start, end - start, StringComparison.Ordinal) < 0;
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        var i = 0;

        while (i < line.Length)
        {
            var symbol = line[i];
            i++;

            if (symbol == '"')
            {
                if (quoted && i < line.Length && line[i] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (symbol == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(symbol);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string Clean(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? "–" : trimmed;
    }
}
