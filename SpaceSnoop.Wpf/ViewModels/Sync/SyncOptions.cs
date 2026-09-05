using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public static class SyncOptions
{
    public static IReadOnlyList<SegmentOption> Modes { get; } =
    [
        new(PackIconLucideKind.ArrowRight, "Слева направо", "Копировать слева направо"),
        new(PackIconLucideKind.ArrowLeft, "Справа налево", "Копировать справа налево"),
        new(PackIconLucideKind.ArrowRightLeft, "Двусторонний", "Синхронизировать в обе стороны"),
    ];

    public static IReadOnlyList<SegmentOption> Winners { get; } =
    [
        new(PackIconLucideKind.Clock, "Новее", "Спорные файлы решает более свежая дата изменения"),
        new(PackIconLucideKind.ArrowLeftToLine, "Слева", "Спорные файлы решает левая сторона"),
        new(PackIconLucideKind.ArrowRightToLine, "Справа", "Спорные файлы решает правая сторона"),
    ];
}
