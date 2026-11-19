using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class NuGetCacheViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public NuGetCacheKind Kind { get; }
    public bool Exists { get; } = true;
    public string Path { get; }
    public long Bytes { get; }
    public int Files { get; }
    public bool CanSelect { get; }
    public string ErrorText { get; }
    public bool HasError => ErrorText.Length > 0;
    public bool IsRecommended => Kind is NuGetCacheKind.HttpCache or NuGetCacheKind.PluginsCache;

    public PackIconLucideKind IconKind => Kind switch
    {
        NuGetCacheKind.HttpCache => PackIconLucideKind.Download,
        NuGetCacheKind.Temp => PackIconLucideKind.Clock,
        NuGetCacheKind.PluginsCache => PackIconLucideKind.Plug,
        _ => PackIconLucideKind.Package,
    };

    public string Summary => !Exists
        ? "Каталог ещё не создан · 0 байт"
        : Kind switch
        {
            NuGetCacheKind.HttpCache => "Повторно загрузится из источников пакетов",
            NuGetCacheKind.Temp => "Только при остановленных restore и сборках",
            NuGetCacheKind.PluginsCache => "Воссоздаётся при следующем обращении NuGet",
            NuGetCacheKind.GlobalPackages => "Потребуется restore и доступ к источникам",
            _ => string.Empty,
        };

    public string SizeText => SizeFormatter.Format(Bytes);
    public string FilesText => $"Файлов: {Files:N0}";

    public string Name => Kind switch
    {
        NuGetCacheKind.HttpCache => "NuGet HTTP Cache",
        NuGetCacheKind.Temp => "NuGet Temp",
        NuGetCacheKind.PluginsCache => "NuGet Plugins Cache",
        NuGetCacheKind.GlobalPackages => "NuGet Global Packages",
        _ => string.Empty,
    };

    public string Description => Kind switch
    {
        NuGetCacheKind.HttpCache => "Данные загрузок из источников пакетов. NuGet скачает их заново.",
        NuGetCacheKind.Temp => "Временные данные и блокировки. Перед очисткой остановите restore, сборки и операции NuGet в IDE.",
        NuGetCacheKind.PluginsCache => "Кэш сведений плагинов NuGet. При следующем обращении он будет создан заново.",
        NuGetCacheKind.GlobalPackages => "Пакеты проектов. Понадобится restore; без сети или доступа к частному источнику восстановление может не получиться.",
        _ => string.Empty,
    };

    public NuGetCacheViewModel(NuGetCacheScan scan)
        : this(scan.Kind, scan.Path, scan.Bytes, scan.Files, scan.CanClean, string.Join(Environment.NewLine, scan.Errors.Concat(scan.Warnings)))
    {
        Exists = scan.Exists;
    }

    internal NuGetCacheViewModel(NuGetCacheKind kind, string path, long bytes, int files, bool canSelect, string errorText)
    {
        Kind = kind;
        Path = path;
        Bytes = bytes;
        Files = files;
        CanSelect = canSelect;
        ErrorText = errorText;
        IsSelected = CanSelect && IsRecommended;
    }
}
