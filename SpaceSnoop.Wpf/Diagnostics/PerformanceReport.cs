using System.Globalization;
using System.Text;

namespace SpaceSnoop.Wpf.Diagnostics;

public static class PerformanceReport
{
    public static string Build(PerformanceSnapshot snapshot, string version)
    {
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.CurrentCulture, $"Производительность SpaceSnoop {version}");

        text.AppendLine(snapshot.CapturedAtUtc == DateTime.MinValue
            ? "Замеров ещё нет"
            : $"Снято: {snapshot.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        text.AppendLine(CultureInfo.CurrentCulture,
            $"Отклик UI: {Math.Round(snapshot.UiDelayMs):N0} мс, пик {Math.Round(snapshot.UiPeakMs):N0} мс, среднее {Math.Round(snapshot.UiAverageMs):N0} мс");

        text.AppendLine(CultureInfo.CurrentCulture,
            $"Окно: {Plural.Format(snapshot.SampleCount, "замер", "замера", "замеров")} за {snapshot.ObservedSpanSeconds:N1} с");

        text.AppendLine(CultureInfo.CurrentCulture,
            $"Память: {SizeFormatter.Format(snapshot.ManagedBytes)} управляемой, {SizeFormatter.Format(snapshot.WorkingSetBytes)} процесс");

        text.AppendLine(CultureInfo.CurrentCulture,
            $"Сборок мусора с начала сбора: {snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections}");

        text.AppendLine(CultureInfo.CurrentCulture, $"Старт приложения: {snapshot.StartupSeconds:N2} с");

        if (PerformanceFormat.Operation(snapshot.Operation) is { } operation)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"Операция: {operation}");
        }

        return text.ToString();
    }
}
