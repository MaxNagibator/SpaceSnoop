using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

        ToolCalls.CollectionChanged += OnToolCallsChanged;
    }

    public ChatRole Role { get; }

    public bool IsUser => Role == ChatRole.User;

    public string AuthorName => IsUser ? "Вы" : AgentPersona.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ShowStatus))]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool _isError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool _isCancelled;

    public bool HasText => Text.Length > 0;

    public bool ShowStatus => IsStreaming;

    public bool CanRetry => !IsUser && (IsError || IsCancelled);

    public string StatusText => ToolCalls.Count > 0 ? AgentPersona.WorkingOn(ToolCalls[^1].Text) : AgentPersona.Thinking;

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

    public void DropPreamble()
    {
        if (_builder.Length == 0)
        {
            return;
        }

        _builder.Clear();
        Text = string.Empty;
    }

    public void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _builder.Append(text);
        Text = _builder.ToString();
    }

    private void OnToolCallsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
    }
}
