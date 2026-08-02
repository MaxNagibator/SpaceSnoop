using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatHistoryViewModel : ObservableObject
{
    private readonly ChatHistoryStore _history;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly AgentBackends _backends;
    private readonly AgentPreferences _preferences;
    private readonly ObservableCollection<ChatMessageViewModel> _messages;
    private readonly Func<bool> _isBusy;
    private readonly Action _refreshLastFlags;
    private readonly Action _notifyMessagesChanged;

    private bool _historyLoaded;
    private string? _conversationId;
    private DateTimeOffset _startedUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryToggleHint))]
    private bool _isHistoryVisible;

    [ObservableProperty]
    private ChatConversationViewModel? _currentConversation;

    internal ChatHistoryViewModel(
        ChatHistoryStore history,
        IDialogService dialogs,
        ILogger logger,
        AgentBackends backends,
        AgentPreferences preferences,
        ObservableCollection<ChatMessageViewModel> messages,
        Func<bool> isBusy,
        Action refreshLastFlags,
        Action notifyMessagesChanged)
    {
        _history = history;
        _dialogs = dialogs;
        _logger = logger;
        _backends = backends;
        _preferences = preferences;
        _messages = messages;
        _isBusy = isBusy;
        _refreshLastFlags = refreshLastFlags;
        _notifyMessagesChanged = notifyMessagesChanged;
        _isHistoryVisible = preferences.HistoryVisible;
    }

    internal string? SessionId { get; set; }

    internal bool SessionDropped { get; set; }

    internal bool ResumedFromDisk { get; set; }

    public ObservableCollection<ChatConversationViewModel> Conversations { get; } = [];

    public bool HasConversations => Conversations.Count > 0;

    public string HistoryToggleHint => IsHistoryVisible ? "Скрыть прошлые разговоры" : "Показать прошлые разговоры";

    private bool CanManageConversation => !_isBusy();

    internal void NotifyBusyChanged()
    {
        NewConversationCommand.NotifyCanExecuteChanged();
        SelectConversationCommand.NotifyCanExecuteChanged();
        DeleteConversationCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    internal void LoadHistory()
    {
        if (_historyLoaded)
        {
            return;
        }

        _historyLoaded = true;

        var records = _history.Load();

        foreach (var record in records)
        {
            Conversations.Add(new(record));
        }

        _logger.ChatHistoryLoaded(records.Count);
        OnPropertyChanged(nameof(HasConversations));
    }

    internal void DiscardEmptyConversation()
    {
        if (CurrentConversation is { } conversation && Conversations.Remove(conversation))
        {
            OnPropertyChanged(nameof(HasConversations));
        }

        NewConversation();
        Persist();
    }

    internal void CaptureTurn()
    {
        if (_messages.Count == 0)
        {
            return;
        }

        _conversationId ??= Guid.NewGuid().ToString("n");
        _startedUtc = _startedUtc == default ? DateTimeOffset.UtcNow : _startedUtc;

        var record = new ChatConversationRecord
        {
            Id = _conversationId,
            Title = ChatHistoryStore.MakeTitle(_messages.FirstOrDefault(message => message.IsUser)?.Text ?? string.Empty),
            Backend = _backends.Current.Kind,
            SessionId = SessionId,
            StartedUtc = _startedUtc,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Messages = [.. _messages.Select(message => message.ToRecord())],
        };

        if (CurrentConversation is { } conversation)
        {
            conversation.Record = record;

            var index = Conversations.IndexOf(conversation);

            if (index > 0)
            {
                Conversations.Move(index, 0);
            }
        }
        else
        {
            var created = new ChatConversationViewModel(record);
            Conversations.Insert(0, created);
            CurrentConversation = created;

            while (Conversations.Count > AppDefaults.AgentHistoryLimit)
            {
                Conversations.RemoveAt(Conversations.Count - 1);
            }

            OnPropertyChanged(nameof(HasConversations));
        }

        Persist();
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private void NewConversation()
    {
        _messages.Clear();
        SessionId = null;
        _conversationId = null;
        _startedUtc = default;
        ResumedFromDisk = false;
        CurrentConversation = null;
        _notifyMessagesChanged();
    }

    [RelayCommand]
    private void ToggleHistory()
    {
        IsHistoryVisible = !IsHistoryVisible;
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private void SelectConversation(ChatConversationViewModel? conversation)
    {
        if (conversation is null || ReferenceEquals(conversation, CurrentConversation))
        {
            return;
        }

        var record = conversation.Record;

        _messages.Clear();

        foreach (var message in record.Messages)
        {
            _messages.Add(ChatMessageViewModel.Restore(message));
        }

        _refreshLastFlags();

        _conversationId = record.Id;
        _startedUtc = record.StartedUtc;
        SessionId = record.Backend == _backends.Current.Kind ? record.SessionId : null;
        ResumedFromDisk = SessionId is { Length: > 0 };
        CurrentConversation = conversation;
        _notifyMessagesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private void DeleteConversation(ChatConversationViewModel? conversation)
    {
        if (conversation is null || !Conversations.Remove(conversation))
        {
            return;
        }

        if (ReferenceEquals(conversation, CurrentConversation))
        {
            NewConversation();
        }

        OnPropertyChanged(nameof(HasConversations));
        Persist();
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private void ClearHistory()
    {
        var count = Conversations.Count;

        if (count == 0)
        {
            return;
        }

        var question = $"Из истории пропадут все разговоры ({count}) вместе с ответами {AgentPersona.NameGenitive}. Вернуть их будет нечем.";

        if (!_dialogs.Confirm("Очистить историю чата", question))
        {
            return;
        }

        Conversations.Clear();
        NewConversation();
        OnPropertyChanged(nameof(HasConversations));
        Persist();
        _logger.ChatHistoryCleared(count);
    }

    private void Persist()
    {
        _history.Save(Conversations.Select(conversation => conversation.Record));
    }

    partial void OnIsHistoryVisibleChanged(bool value)
    {
        _preferences.HistoryVisible = value;
    }

    partial void OnCurrentConversationChanged(ChatConversationViewModel? oldValue, ChatConversationViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsCurrent = false;
        }

        if (newValue is not null)
        {
            newValue.IsCurrent = true;
        }
    }
}
