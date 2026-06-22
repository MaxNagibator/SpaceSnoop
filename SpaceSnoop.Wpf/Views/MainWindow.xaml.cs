using System.ComponentModel;

namespace SpaceSnoop.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly ShellPreferences _preferences;
    private readonly ISettingsStore _settings;

    public MainWindow(ShellViewModel viewModel, ShellPreferences preferences, ISettingsStore settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _preferences = preferences;
        _settings = settings;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        SourceInitialized += (_, _) => WindowChromeTheming.SetBackdrop(this, _preferences.Backdrop);
        _preferences.PropertyChanged += OnPreferencesChanged;

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

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellPreferences.Backdrop))
        {
            WindowChromeTheming.SetBackdrop(this, _preferences.Backdrop);
        }
    }
}
