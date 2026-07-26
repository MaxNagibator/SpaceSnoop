using System.Collections.ObjectModel;
using System.Text;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatMessageViewModel : ObservableObject
{
    private readonly StringBuilder _builder = new();

    public ChatMessageViewModel(ChatRole role, string text = "")
    {
        Role = role;

        if (text.Length > 0)
        {
            _builder.Append(text);
            _text = text;
        }
    }

    public ChatRole Role { get; }

    public bool IsUser => Role == ChatRole.User;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isCancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCost))]
    private double _costUsd;

    public bool HasCost => CostUsd > 0;

    public ObservableCollection<ChatToolCall> ToolCalls { get; } = [];

    public void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _builder.Append(text);
        Text = _builder.ToString();
    }
}
