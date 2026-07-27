namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatConversationViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle))]
    private ChatConversationRecord _record;

    [ObservableProperty]
    private bool _isCurrent;

    public ChatConversationViewModel(ChatConversationRecord record)
    {
        _record = record;
    }

    public string Id => Record.Id;

    public string Title => Record.Title.Length > 0 ? Record.Title : ChatHistoryStore.UntitledConversation;

    public string Subtitle => $"{Record.Messages.Count} {Messages(Record.Messages.Count)} · {Record.UpdatedUtc.ToLocalTime():d MMMM, HH:mm}";

    private static string Messages(int count)
    {
        if (count % 100 is >= 11 and <= 14)
        {
            return "сообщений";
        }

        return (count % 10) switch
        {
            1 => "сообщение",
            2 or 3 or 4 => "сообщения",
            _ => "сообщений",
        };
    }
}
