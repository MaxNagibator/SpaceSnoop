using Microsoft.Win32;
using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public sealed class SystemTheme(IUiDispatcher dispatcher) : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private bool _watching;

    public static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return IsLight(key?.GetValue(AppsUseLightThemeValue));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return true;
        }
    }

    public static bool IsLight(object? appsUseLightTheme)
    {
        return appsUseLightTheme is not int value || value != 0;
    }

    public void Watch()
    {
        if (_watching)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watching = true;

        var light = AppsUseLightTheme();
        dispatcher.Invoke(() => AppThemes.FollowSystem(light));
    }

    public void Dispose()
    {
        if (!_watching)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watching = false;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        var light = AppsUseLightTheme();
        dispatcher.Invoke(() => AppThemes.FollowSystem(light));
    }
}
