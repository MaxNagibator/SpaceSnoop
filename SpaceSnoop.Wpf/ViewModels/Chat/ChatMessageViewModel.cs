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
    [NotifyPropertyChangedFor(nameof(HasUsage), nameof(UsageText))]
    private double _costUsd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUsage), nameof(UsageText))]
    private long _tokens;

    public bool HasUsage => CostUsd > 0 || Tokens > 0;

    public string UsageText => CostUsd > 0
        ? $"Стоимость хода: ${CostUsd:0.0000}"
        : $"Токенов за ход: {Tokens:N0}";

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
