using SpaceSnoop.Wpf.Bootstrap.Platform;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncProfileValidation
{
    internal static string FormatMirrorWarning(bool withoutRecycleBin, bool permanentNetworkDelete)
    {
        if (withoutRecycleBin)
        {
            return permanentNetworkDelete
                ? "На сетевом хранилище нет корзины Windows. При запуске этого профиля лишние файлы будут удалены безвозвратно."
                : "На сетевом хранилище нет корзины Windows. Для удаления лишних файлов разрешите безвозвратное удаление в настройках профиля.";
        }

        return permanentNetworkDelete
            ? "Включено «Зеркало»: лишние файлы удаляются в корзину, а на сетевом приёмнике без корзины – безвозвратно."
            : "Включено «Зеркало»: лишние файлы удаляются в корзину. На сетевом приёмнике без корзины потребуется отдельное разрешение.";
    }

    internal static async Task<string?> RefuseSchedulingAsync(
        Func<(string Left, string Right, bool TimeInvalid)> capture,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var input = capture();

            if (input.Left.Length == 0 || input.Right.Length == 0)
            {
                return "Укажите оба каталога перед включением.";
            }

            if (UncPath.IsIncompleteUnc(input.Left) || UncPath.IsIncompleteUnc(input.Right))
            {
                return "Укажите полный сетевой путь вида \\\\сервер\\шара.";
            }

            var leftProbe = await PathProbe.CheckAsync(input.Left, timeoutMs, cancellationToken);

            if (leftProbe.Status == PathProbeStatus.Cancelled)
            {
                return null;
            }

            if (input != capture())
            {
                continue;
            }

            if (leftProbe.Status == PathProbeStatus.TimedOut)
            {
                return "Сетевой каталог не отвечает (превышено время ожидания).";
            }

            if (leftProbe.Status != PathProbeStatus.Exists)
            {
                return "Один из каталогов не существует.";
            }

            var rightProbe = await PathProbe.CheckAsync(input.Right, timeoutMs, cancellationToken);

            if (rightProbe.Status == PathProbeStatus.Cancelled)
            {
                return null;
            }

            if (input != capture())
            {
                continue;
            }

            if (rightProbe.Status == PathProbeStatus.TimedOut)
            {
                return "Сетевой каталог не отвечает (превышено время ожидания).";
            }

            if (rightProbe.Status != PathProbeStatus.Exists)
            {
                return "Один из каталогов не существует.";
            }

            string? refusal;

            try
            {
                refusal = await Task.Run(() => SyncRootsCheck.Refusal(input.Left, input.Right), cancellationToken)
                    .WaitAsync(TimeSpan.FromMilliseconds(Math.Max(1, timeoutMs)), cancellationToken);
            }
            catch (TimeoutException)
            {
                return "Сетевой каталог не отвечает (превышено время ожидания).";
            }

            if (input != capture())
            {
                continue;
            }

            if (refusal is not null)
            {
                return refusal;
            }

            return input.TimeInvalid ? "Время укажите в формате ЧЧ:ММ, например 03:00." : null;
        }
    }
}
