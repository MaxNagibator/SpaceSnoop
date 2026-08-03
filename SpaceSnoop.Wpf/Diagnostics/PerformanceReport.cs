using System.Globalization;
using System.Text;

namespace SpaceSnoop.Wpf.Diagnostics;

public static class PerformanceReport
{
    public static string Build(PerformanceSnapshot snapshot, string version, PerformanceOperation? lastRun = null)
    {
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.CurrentCulture, $"Производительность SpaceSnoop {version}");

        if (snapshot.SampleCount == 0)
        {
            text.AppendLine("Замеров ещё нет: сбор только запущен или сброшен, отклик мерить не по чему");
        }
        else
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"Снято: {snapshot.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

            text.AppendLine(CultureInfo.CurrentCulture,
                $"Отклик UI: {Math.Round(snapshot.UiDelayMs):N0} мс, пик {Math.Round(snapshot.UiPeakMs):N0} мс, среднее {Math.Round(snapshot.UiAverageMs):N0} мс");

            text.AppendLine(CultureInfo.CurrentCulture,
                $"Окно: {Plural.Format(snapshot.SampleCount, "замер", "замера", "замеров")} за {snapshot.ObservedSpanSeconds:N1} с");
        }

        text.AppendLine(CultureInfo.CurrentCulture,
            $"Память: {SizeFormatter.Format(snapshot.WorkingSetBytes)} процесс, {SizeFormatter.Format(snapshot.ManagedBytes)} управляемой, {PerformanceFormat.TileMemoryPeak(snapshot)}");

        text.AppendLine(CultureInfo.CurrentCulture,
            $"Сборок мусора с начала сбора: {snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections} ({PerformanceFormat.TileCollectionsWindow(snapshot)})");

        text.AppendLine(CultureInfo.CurrentCulture, $"Старт приложения: {snapshot.StartupSeconds:N2} с");

        if (snapshot.FrameCount == 0)
        {
            text.AppendLine("Кадры окна: не измерялись");
        }
        else
        {
            text.AppendLine(CultureInfo.CurrentCulture,
                $"Кадры окна: пик {snapshot.FramePeakMs:N1} мс, среднее {snapshot.FrameAverageMs:N1} мс за {Plural.Format(snapshot.FrameCount, "кадр", "кадра", "кадров")}, дольше {AppDefaults.PerformanceFrameSlowMs:N0} мс – {snapshot.SlowFrameCount}");
        }

        var tile = PerformanceFormat.TileOperation(snapshot.Operation, lastRun);
        text.AppendLine(CultureInfo.CurrentCulture, $"{tile.Caption} – {tile.Value}, {tile.Volume}, {tile.Rate}");

        return text.ToString();
    }
}
