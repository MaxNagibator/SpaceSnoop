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
    [NotifyPropertyChangedFor(nameof(HasText), nameof(LooksTruncated))]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ShowStatus), nameof(CanRetry), nameof(LooksTruncated))]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry), nameof(LooksTruncated))]
    private bool _isError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry), nameof(LooksTruncated))]
    private bool _isCancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private bool _isLast;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTranscript))]
    private string? _transcriptPath;

    public bool HasText => Text.Length > 0;

    public bool ShowStatus => IsStreaming;

    public bool CanRetry => !IsUser && IsLast && !IsStreaming;

    public bool CanRewind => IsUser;

    public bool LooksTruncated => !IsUser && !IsStreaming && !IsError && !IsCancelled && ChatAnswer.LooksTruncated(Text);

    public bool HasTranscript => TranscriptPath is { Length: > 0 };

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

    public static ChatMessageViewModel Restore(ChatMessageRecord record)
    {
        var message = new ChatMessageViewModel(record.Role, record.Text)
        {
            IsError = record.IsError,
            IsCancelled = record.IsCancelled,
            CostUsd = record.CostUsd,
            Tokens = record.Tokens,
            TranscriptPath = record.TranscriptPath,
        };

        foreach (var tool in record.Tools)
        {
            message.ToolCalls.Add(ChatToolCall.From(tool));
        }

        return message;
    }

    public ChatMessageRecord ToRecord()
    {
        return new()
        {
            Role = Role,
            Text = Text,
            IsError = IsError,
            IsCancelled = IsCancelled,
            CostUsd = CostUsd,
            Tokens = Tokens,
            TranscriptPath = TranscriptPath,
            Tools = [.. ToolCalls.Select(call => call.Name)],
        };
    }

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
