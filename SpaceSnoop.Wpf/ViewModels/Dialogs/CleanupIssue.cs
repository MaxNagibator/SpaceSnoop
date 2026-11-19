using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class CleanupIssue(string name, string reason, string details) : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public string Name { get; } = name;

    public string Reason { get; } = reason;

    public string Details { get; } = details;

    public static CleanupIssue From(string details)
    {
        var end = details.IndexOf("»: ", StringComparison.Ordinal);
        if (!details.StartsWith('«') || end < 2)
        {
            return new("Ошибка очистки", "Не удалось очистить объект", details);
        }

        var subject = details[1..end];
        var message = details[(end + 3)..];
        if (Enum.GetValues<CleanupAvailability>().Any(availability =>
            availability != CleanupAvailability.Available && message == CleanupText.Availability(availability)))
        {
            return new(subject, char.ToUpperInvariant(message[0]) + message[1..], details);
        }

        var name = Path.GetFileName(subject.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var reason = message switch
        {
            "файл изменился после обхода" => "Изменился после замера",
            "каталог не читается" => "Не удалось прочитать каталог",
            _ when message.StartsWith("каталог не удалён – ", StringComparison.Ordinal) => "Не удалось удалить каталог",
            _ => "Не удалось удалить",
        };

        return new(name.Length > 0 ? name : subject, reason, details);
    }
}
