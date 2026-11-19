using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    public string PageTitle => "Сканирование";

    public string PageDescription => "Анализ занятого места по дискам и каталогам.";

    public bool IsIndeterminate => IsExporting ? ExportProgressPercent == 0 : Progress.IsIndeterminate;

    public double ProgressValue => IsExporting ? ExportProgressPercent / 100.0 : Progress.ProgressValue;

    public double ProgressMax => Progress.ProgressMax;

    public ICommand CancelCommand => IsExporting ? CancelFullScanExportCommand : StopCommand;

    private void OnProgressPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsIndeterminate) or nameof(ProgressValue))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }
}
