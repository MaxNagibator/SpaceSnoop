using System.Globalization;
using System.Text;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed record ScheduleStatus
{
    public const int NeverRun = 267011;
    public const int Running = 267009;

    public bool Exists { get; init; }
    public string NextRun { get; init; } = "–";
    public string LastRun { get; init; } = "–";
    public int LastResult { get; init; }
    public string LastResultText { get; init; } = "–";

    public static ScheduleStatus Missing { get; } = new();

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
            NeverRun => "Ещё не запускалась",
            Running => "Выполняется",
            _ => $"Код {code}",
        };
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var symbol = line[i];

            if (symbol == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
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
