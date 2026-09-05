using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
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
}
