using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatViewModel : ObservableObject, IPageHeader
{
    private readonly AgentBackends _backends;
    private readonly AgentPreferences _preferences;
    private readonly McpBridge _bridge;
    private readonly ChatHistoryStore _history;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ChatViewModel> _logger;

    private bool _detectStarted;
    private bool _historyLoaded;
    private int _detectGeneration;
    private string? _sessionId;
    private bool _sessionDropped;
    private string? _conversationId;
    private DateTimeOffset _startedUtc;
    private bool _resumedFromDisk;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCliMissingBanner), nameof(ShowConsentBanner), nameof(ShowMcpBanner), nameof(CanChat), nameof(ShowShellBanner))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private AgentCliInfo? _cliInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCliMissingBanner), nameof(ShowShellBanner))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isDetectingCli;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(
        nameof(SendCommand),
        nameof(CancelCommand),
        nameof(NewConversationCommand),
        nameof(RetryCommand),
        nameof(SelectConversationCommand),
        nameof(DeleteConversationCommand),
        nameof(ClearHistoryCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryToggleHint))]
    private bool _isHistoryVisible;

    [ObservableProperty]
    private ChatConversationViewModel? _currentConversation;

    public ChatViewModel(
        AgentBackends backends,
        AgentPreferences preferences,
        AgentModelSelector agentModel,
        McpPreferences mcp,
        McpServerHost mcpServer,
        McpBridge bridge,
        ChatHistoryStore history,
        IDialogService dialogs,
        ILogger<ChatViewModel> logger)
    {
        _backends = backends;
        _preferences = preferences;
        _bridge = bridge;
        _history = history;
        _dialogs = dialogs;
        AgentModel = agentModel;
        Mcp = mcp;
        McpServer = mcpServer;
        _logger = logger;
        _isHistoryVisible = preferences.HistoryVisible;

        _preferences.PropertyChanged += OnGateSourceChanged;
        Mcp.PropertyChanged += OnGateSourceChanged;
        McpServer.PropertyChanged += OnGateSourceChanged;
    }

    public event Action? FocusRequested;

    public static IReadOnlyList<ChatExample> Examples { get; } =
    [
        new("Куда делось место на диске C?", PackIconLucideKind.HardDrive),
        new("Что тут можно снести без последствий?", PackIconLucideKind.Trash2),
        new("Сколько места занял Docker и сколько из него вернётся?", PackIconLucideKind.Container),
        new("Почему эти папки опять расходятся после синхронизации?", PackIconLucideKind.FolderSync),
    ];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChatConversationViewModel> Conversations { get; } = [];

    public AgentModelSelector AgentModel { get; }

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

    public string PageTitle => "Чат";

    public string? PageDescription => $"{AgentPersona.Name} – агент внутри программы: смотрит на неё теми же инструментами, что и MCP-сервер.";

    public string AgentName => AgentPersona.Name;

    public string Greeting => AgentPersona.Greeting;

    public string InputPlaceholder => AgentPersona.InputPlaceholder;

    public bool HasMessages => Messages.Count > 0;

    public bool HasConversations => Conversations.Count > 0;

    public string HistoryToggleHint => IsHistoryVisible ? "Скрыть прошлые разговоры" : "Показать прошлые разговоры";

    public bool ShowCliMissingBanner => !IsDetectingCli && CliInfo is null;

    public bool ShowConsentBanner => !IsDetectingCli && CliInfo is not null && !_preferences.Consent;

    public bool ShowMcpBanner => !IsDetectingCli && CliInfo is not null && _preferences.Consent && !McpReady;

    public bool CanChat => !IsDetectingCli && CliInfo is not null && _preferences.Consent && McpReady;

    public bool MutationsAllowed => Mcp.AllowMutations;

    public string BackendName => Backend.DisplayName;

    public string DetectingCliText => $"Проверяю, установлен ли {Backend.DisplayName}…";

    public string CliMissingTitle => $"{Backend.DisplayName} не найден";

    public string CliMissingText => $"Чат работает через уже установленный на этой машине CLI {Backend.DisplayName} – по вашей подписке, без ключей в настройках. Установите CLI и войдите в свою подписку, затем проверьте снова: {Backend.MissingCliHint}";

    public string ConsentText => $"Текст сообщения и то, что агент запрашивает у инструментов приложения (пути, размеры, результаты сравнения), уходит в CLI {Backend.DisplayName} и дальше поставщику модели – под вашей собственной подпиской, не по ключу приложения.";

    public bool ShowShellBanner => Backend.HasBuiltInShell && CanChat;

    public string ShellNotice => $"У CLI {Backend.DisplayName} есть собственная оболочка операционной системы, и отключить её нечем: помимо инструментов приложения {AgentPersona.Name} может выполнять команды с правами SpaceSnoop. Запуск команды виден в ленте отдельным бейджем.";

    public string ShellNoticeShort => $"{Backend.DisplayName}: у {AgentPersona.NameGenitive} есть оболочка системы – он может выполнять команды с правами SpaceSnoop";

    public string MutationsNoticeShort => $"Изменяющие операции разрешены: {AgentPersona.Name} может сам запустить синхронизацию, упаковать каталог в архив и пометить лишнее на удаление";

    public string MutationsNotice => $"Изменяющие операции разрешены в настройках: {AgentPersona.Name} может сам запустить синхронизацию (файлы скопируются, лишние уйдут в корзину) и упаковать каталог в архив. Сначала он обязан показать план и дождаться вашего согласия. Пометки на удаление он тоже ставит сам, но удаляет помеченное только человек.";

    public string EmptyStateHint => MutationsAllowed ? AgentPersona.MutationsNote : AgentPersona.ReadOnlyNote;

    private IAgentBackend Backend => _backends.Current;

    private bool McpReady => Mcp.Enabled && McpServer.IsRunning && !string.IsNullOrWhiteSpace(Mcp.Token);

    private bool CanSend => !IsBusy && CanChat && !string.IsNullOrWhiteSpace(InputText);

    private bool CanCancel => IsBusy;

    private bool CanManageConversation => !IsBusy;

    public void PrepareQuestion(string question)
    {
        if (question.Length == 0)
        {
            return;
        }

        InputText = question;
        _logger.AgentQuestionPrefilled(question);
        FocusRequested?.Invoke();
    }

    public async Task EnsureLoadedAsync()
    {
        LoadHistory();

        if (_detectStarted)
        {
            return;
        }

        _detectStarted = true;
        await DetectCliAsync();
    }

    [RelayCommand]
    private async Task RecheckCliAsync()
    {
        await DetectCliAsync();
    }

    private async Task DetectCliAsync()
    {
        var backend = Backend;
        var generation = ++_detectGeneration;

        IsDetectingCli = true;

        try
        {
            AgentCliInfo? info;

            try
            {
                info = await Task.Run(backend.Detect);
            }
            catch (Exception exception)
            {
                _logger.AgentCliDetectionFailed(exception, backend.DisplayName);
                info = null;
            }

            if (generation == _detectGeneration)
            {
                CliInfo = info;
            }

            if (info is not null && generation == _detectGeneration)
            {
                await AgentModel.EnsureModelsAsync();
            }
        }
        finally
        {
            if (generation == _detectGeneration)
            {
                IsDetectingCli = false;
            }
        }
    }

    [RelayCommand]
    private void GrantConsent()
    {
        _preferences.Consent = true;
        _logger.AgentConsentGranted();
    }

    [RelayCommand]
    private void EnableMcp()
    {
        Mcp.Enabled = true;
    }

    [RelayCommand]
    private void UseExample(string? example)
    {
        InputText = example ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private void NewConversation()
    {
        Messages.Clear();
        _sessionId = null;
        _conversationId = null;
        _startedUtc = default;
        _resumedFromDisk = false;
        CurrentConversation = null;
        OnPropertyChanged(nameof(HasMessages));
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

        Messages.Clear();

        foreach (var message in record.Messages)
        {
            Messages.Add(ChatMessageViewModel.Restore(message));
        }

        _conversationId = record.Id;
        _startedUtc = record.StartedUtc;
        _sessionId = record.Backend == Backend.Kind ? record.SessionId : null;
        _resumedFromDisk = _sessionId is { Length: > 0 };
        CurrentConversation = conversation;
        OnPropertyChanged(nameof(HasMessages));
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

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void CopyMessage(ChatMessageViewModel? message)
    {
        if (message is null || message.Text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(message.Text);
        }
        catch (Exception exception)
        {
            _logger.AgentMessageCopyFailed(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private async Task RetryAsync(ChatMessageViewModel? message)
    {
        if (message is null || !CanChat)
        {
            return;
        }

        var index = Messages.IndexOf(message);

        if (index < 0)
        {
            return;
        }

        var prompt = Messages.Take(index).LastOrDefault(candidate => candidate.IsUser)?.Text;

        if (prompt is not { Length: > 0 })
        {
            return;
        }

        await RunTurnAsync(prompt);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var prompt = InputText.Trim();

        if (prompt.Length == 0)
        {
            return;
        }

        InputText = string.Empty;
        await RunTurnAsync(prompt);
    }

    private async Task RunTurnAsync(string prompt)
    {
        Messages.Add(new ChatMessageViewModel(ChatRole.User, prompt));

        var assistant = new ChatMessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Messages.Add(assistant);
        OnPropertyChanged(nameof(HasMessages));

        var backend = Backend;
        var mutations = Mcp.AllowMutations;
        var allowed = AgentPrompt.AllowedTools(mutations);

        if (mutations)
        {
            _logger.AgentMutationsGranted(string.Join(", ", AgentPrompt.Destructive));
        }

        var request = new AgentRequest
        {
            Prompt = prompt,
            Context = _bridge.DescribeContext(),
            ResumeSessionId = _sessionId,
            SystemPrompt = AgentPrompt.Build(mutations, backend.HasBuiltInShell),
            Model = _preferences.ModelFor(backend.Kind) is { Length: > 0 } model ? model : null,
            Effort = _preferences.EffortFor(backend.Kind) is { Length: > 0 } effort ? effort : null,
            Mcp = new AgentMcpConfig(AgentPrompt.ServerName, McpServer.Endpoint ?? string.Empty, Mcp.Token, allowed, AgentPrompt.DeniedTools(mutations)),
        };

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var resumedFromDisk = _resumedFromDisk && _sessionId is { Length: > 0 };

        _sessionDropped = false;
        IsBusy = true;

        try
        {
            await foreach (var turnEvent in backend.RunAsync(request, token).WithCancellation(token))
            {
                switch (turnEvent.Kind)
                {
                    case AgentEventKind.Started:
                        if (!_sessionDropped)
                        {
                            _sessionId = turnEvent.SessionId;
                        }

                        break;

                    case AgentEventKind.Text:
                        assistant.Append(turnEvent.Text);
                        break;

                    case AgentEventKind.ToolCall:
                        assistant.DropPreamble();
                        assistant.ToolCalls.Add(ChatToolCall.From(turnEvent.ToolName));
                        break;

                    case AgentEventKind.Completed:
                        if (!_sessionDropped)
                        {
                            _sessionId = turnEvent.SessionId ?? _sessionId;
                        }

                        assistant.CostUsd = turnEvent.CostUsd;
                        assistant.Tokens = turnEvent.Tokens;
                        break;

                    case AgentEventKind.Failed:
                        assistant.IsError = true;
                        assistant.Append(assistant.Text.Length > 0 ? $"\n\n{turnEvent.Text}" : turnEvent.Text);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            assistant.IsCancelled = true;
        }
        catch (Exception exception)
        {
            assistant.IsError = true;
            assistant.Append(assistant.Text.Length > 0 ? $"\n\n{exception.Message}" : exception.Message);
        }
        finally
        {
            assistant.IsStreaming = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (resumedFromDisk)
        {
            _resumedFromDisk = false;

            if (assistant.IsError)
            {
                _sessionId = null;
                _logger.ChatRestoredSessionDropped();
            }
        }

        CaptureTurn();
    }

    private void LoadHistory()
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

    private void CaptureTurn()
    {
        if (Messages.Count == 0)
        {
            return;
        }

        _conversationId ??= Guid.NewGuid().ToString("n");
        _startedUtc = _startedUtc == default ? DateTimeOffset.UtcNow : _startedUtc;

        var record = new ChatConversationRecord
        {
            Id = _conversationId,
            Title = ChatHistoryStore.MakeTitle(Messages.FirstOrDefault(message => message.IsUser)?.Text ?? string.Empty),
            Backend = Backend.Kind,
            SessionId = _sessionId,
            StartedUtc = _startedUtc,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Messages = [.. Messages.Select(message => message.ToRecord())],
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

    private void OnGateSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _preferences))
        {
            if (e.PropertyName == nameof(AgentPreferences.Enabled) && !_preferences.Enabled)
            {
                _cts?.Cancel();
            }

            if (e.PropertyName == nameof(AgentPreferences.Backend))
            {
                SwitchBackend();
                return;
            }
        }

        if (ReferenceEquals(sender, Mcp) && e.PropertyName == nameof(McpPreferences.AllowMutations) && !Backend.SendsSystemPromptEachTurn)
        {
            _sessionId = null;
            _sessionDropped = IsBusy;
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(NotifyGatesChanged);
            return;
        }

        NotifyGatesChanged();
    }

    private void SwitchBackend()
    {
        _cts?.Cancel();
        _sessionId = null;
        _logger.AgentBackendChanged(Backend.DisplayName);

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(ReloadBackend);
            return;
        }

        ReloadBackend();
    }

    private void ReloadBackend()
    {
        NotifyGatesChanged();
        NotifyBackendChanged();

        if (_detectStarted)
        {
            _ = DetectCliAsync();
        }
    }

    private void NotifyBackendChanged()
    {
        OnPropertyChanged(nameof(BackendName));
        OnPropertyChanged(nameof(DetectingCliText));
        OnPropertyChanged(nameof(CliMissingTitle));
        OnPropertyChanged(nameof(CliMissingText));
        OnPropertyChanged(nameof(ConsentText));
        OnPropertyChanged(nameof(ShowShellBanner));
        OnPropertyChanged(nameof(ShellNotice));
        OnPropertyChanged(nameof(ShellNoticeShort));
    }

    private void NotifyGatesChanged()
    {
        OnPropertyChanged(nameof(ShowConsentBanner));
        OnPropertyChanged(nameof(ShowMcpBanner));
        OnPropertyChanged(nameof(CanChat));
        OnPropertyChanged(nameof(ShowShellBanner));
        OnPropertyChanged(nameof(MutationsAllowed));
        OnPropertyChanged(nameof(EmptyStateHint));
        SendCommand.NotifyCanExecuteChanged();
    }
}
