using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ToastNotifierTests
{
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void При_Notify_тост_добавляется_только_если_уведомления_включены(bool enabled, int expectedCount)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.EnableToastNotifications, enabled ? "true" : "false");
        var prefs = new ShellPreferences(settings);
        var toasts = new ToastHostViewModel();
        var notifier = new ToastNotifier(toasts, prefs);

        notifier.Notify("Тест");

        Assert.That(toasts.Toasts, Has.Count.EqualTo(expectedCount));
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public event EventHandler<string>? Changed;

        public string FilePath => string.Empty;

        public string? GetStringValue(string key)
        {
            return _values.GetValueOrDefault(key);
        }

        public void SetValue(string key, string value)
        {
            _values[key] = value;
            Changed?.Invoke(this, key);
        }

        public void Flush() { }
    }
}
