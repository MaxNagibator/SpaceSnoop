using System.Windows;

namespace SpaceSnoop.Wpf.Bootstrap;

/// <summary>
/// Словари ресурсов приложения одним списком: их берёт и <see cref="App"/> на старте, и тестовый
/// <c>TestApplication.Ensure</c>. Список живёт здесь, а не в <c>App.xaml</c>, потому что тесту
/// прикладной <c>App</c> брать нельзя, а второй список в тестах разъехался бы с этим молча.
/// <para>
/// Словари слиты <b>плоско</b>, и это условие работы: <c>BasedOn="{StaticResource …}"</c> из
/// <c>Shared.xaml</c> на каркасный стиль – отложенная ссылка, и резолвится она в области видимости
/// того словаря, в который слит её носитель. Спрятать список во вложенный словарь-агрегатор
/// значит увести каркасные стили из этой области: <c>ToggleButton.Filter</c> перестаёт строиться с
/// «не удаётся найти ресурс ToggleButton.Toolbar», причём только в момент первого обращения.
/// </para>
/// </summary>
public static class AppResources
{
    public static readonly Uri[] Sources =
    [
        new("pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml"),
        new("pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml"),
        new("pack://application:,,,/KeepShell;component/Resources/Converters.xaml"),
        new("pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml"),
        new("pack://application:,,,/SpaceSnoop.Wpf;component/Resources/Styles/Segments.xaml"),
        new("pack://application:,,,/SpaceSnoop.Wpf;component/Resources/Styles/Trees.xaml"),
        new("pack://application:,,,/SpaceSnoop.Wpf;component/Resources/Styles/Shared.xaml"),
    ];

    public static void InstallInto(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var source in Sources)
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
        }
    }
}
