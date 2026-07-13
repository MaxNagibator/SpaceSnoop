using System.ComponentModel;

namespace SpaceSnoop.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly ISettingsStore _settings;

    public MainWindow(ShellViewModel viewModel, ISettingsStore settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        WindowPlacement.Restore(this, _settings, WindowKeys);
    }

    private static WindowPlacementKeys WindowKeys => new(SettingsKeys.WindowLeft,
        SettingsKeys.WindowTop,
        SettingsKeys.WindowWidth,
        SettingsKeys.WindowHeight,
        SettingsKeys.WindowMaximized);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.RequestClose())
        {
            e.Cancel = true;
            return;
        }

        WindowPlacement.Save(this, _settings, WindowKeys);

        base.OnClosing(e);
    }
}
