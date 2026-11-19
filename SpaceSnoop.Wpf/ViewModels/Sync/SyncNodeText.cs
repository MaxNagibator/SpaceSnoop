using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncNodeText
{
    internal static string DescribeDiff(FileComparison file)
    {
        if (file.TypeConflict != FileTypeConflict.None)
        {
            return file.TypeConflict switch
            {
                FileTypeConflict.LeftFileRightDirectory => "слева файл, справа каталог",
                FileTypeConflict.LeftLinkRightObject => "слева ссылка, справа настоящий объект",
                FileTypeConflict.RightLinkLeftObject => "справа ссылка, слева настоящий объект",
                FileTypeConflict.CaseCollision => "имена различаются только регистром",
                _ => "слева каталог, справа файл",
            };
        }

        if (file.Status != ComparisonStatus.Modified)
        {
            return string.Empty;
        }

        var sizeDiffers = file.LeftSize != file.RightSize;
        var delta = file is { LeftModified: { } left, RightModified: { } right } ? (left - right).Duration() : TimeSpan.Zero;
        var timeDiffers = delta > DirectoryComparer.FatTimestampTolerance;

        return (sizeDiffers, timeDiffers) switch
        {
            (true, true) => $"размер и время (Δ {FormatDelta(delta)})",
            (true, false) => "размер",
            (false, true) => $"время (Δ {FormatDelta(delta)})",
            _ => string.Empty,
        };
    }

    internal static string FormatDelta(TimeSpan delta)
    {
        var d = delta.Duration();

        if (d.TotalSeconds < 60)
        {
            return $"{(int)Math.Round(d.TotalSeconds)} с";
        }

        if (d.TotalMinutes < 60)
        {
            return $"{(int)Math.Round(d.TotalMinutes)} мин";
        }

        return $"{d.TotalHours:0.#} ч";
    }

    internal static string FormatModified(DateTime? value)
    {
        return value is { } dt ? dt.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
    }

    internal static PackIconLucideKind IconFor(SyncAction action)
    {
        return action switch
        {
            SyncAction.CopyToRight => PackIconLucideKind.ArrowRight,
            SyncAction.CopyToLeft => PackIconLucideKind.ArrowLeft,
            SyncAction.Skip => PackIconLucideKind.Ban,
            SyncAction.DeleteLeft or SyncAction.DeleteRight => PackIconLucideKind.Trash2,
            _ => PackIconLucideKind.Zap,
        };
    }

    internal static string OneSidedDirectoryHint(SyncAction action)
    {
        return action switch
        {
            SyncAction.CopyToRight => "Каталог только слева – создать справа (клик меняет)",
            SyncAction.CopyToLeft => "Каталог только справа – создать слева (клик меняет)",
            SyncAction.DeleteLeft or SyncAction.DeleteRight => "Каталог будет удалён в корзину (клик: копировать)",
            _ => "Каталог пропускается (клик: копировать)",
        };
    }

    internal static string SubtreeHint(SyncAction? action)
    {
        return action switch
        {
            SyncAction.CopyToRight => "Всё слева направо – клик меняет",
            SyncAction.CopyToLeft => "Всё справа налево – клик меняет",
            SyncAction.Skip => "Всё пропустить – клик меняет",
            SyncAction.DeleteLeft or SyncAction.DeleteRight => "Всё удалить – клик задаёт «всё копировать →»",
            _ => "Разные действия – клик задаёт «всё копировать →»",
        };
    }

    internal static string FileActionHint(SyncAction action)
    {
        return action switch
        {
            SyncAction.CopyToRight => "Копировать слева направо",
            SyncAction.CopyToLeft => "Копировать справа налево",
            SyncAction.Skip => "Пропустить",
            SyncAction.DeleteLeft => "Удалить слева",
            SyncAction.DeleteRight => "Удалить справа",
            _ => "Действие не задано – клик выбирает следующее",
        };
    }
}
