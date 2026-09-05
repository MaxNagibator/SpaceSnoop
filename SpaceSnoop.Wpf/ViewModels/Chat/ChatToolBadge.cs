namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatToolBadge : ObservableObject
{
    private readonly List<string> _details = [];

    public ChatToolBadge(ChatToolCall call)
    {
        Name = call.Name;
        Text = call.Text;
        IsMutating = call.IsMutating;

        Add(call);
    }

    public string Name { get; }

    public string Text { get; }

    public bool IsMutating { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private int _count;

    [ObservableProperty]
    private string _hint = string.Empty;

    public string Label => Count > 1 ? $"{Text} ×{Count}" : Text;

    public void Add(ChatToolCall call)
    {
        Count++;

        if (call.Details is { Length: > 0 } details)
        {
            _details.Add(details);
        }

        Hint = _details.Count > 0 ? string.Join(Environment.NewLine, _details) : Text;
    }
}
