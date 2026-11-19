namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class DeveloperCleanupViewModel
{
    internal void ShowForAutomation()
    {
        SelectedAreaIndex = 0;
        Projects.ResetForAutomation();
        Workloads.ResetForAutomation();
        DotNetClean.ResetForAutomation();
        _uiDispatcher.Invoke(() =>
        {
            _loaded = true;
            _plan = null;
            ClearRows();
            var rows = new[]
            {
                new NuGetCacheViewModel(NuGetCacheKind.HttpCache, @"C:\Users\Developer\AppData\Local\NuGet\v3-cache", 320_000_000, 1_240, true, string.Empty),
                new NuGetCacheViewModel(NuGetCacheKind.Temp, @"C:\Users\Developer\AppData\Local\Temp\NuGetScratch", 48_000_000, 36, true, string.Empty),
                new NuGetCacheViewModel(NuGetCacheKind.PluginsCache, @"C:\Users\Developer\AppData\Local\NuGet\plugins-cache", 12_000_000, 90, true, string.Empty),
                new NuGetCacheViewModel(NuGetCacheKind.GlobalPackages, @"C:\Users\Developer\.nuget\packages", 4_020_000_000, 18_540, true, string.Empty),
            };

            foreach (var row in rows)
            {
                row.PropertyChanged += OnSelectionChanged;
                Caches.Add(row);
            }

            StatusText = "Пример результатов замера. Очистка в демонстрации недоступна.";
            NotifySelection();
        });
    }
}
