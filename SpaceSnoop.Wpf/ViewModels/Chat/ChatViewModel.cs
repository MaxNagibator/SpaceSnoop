using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatViewModel : ObservableObject, IPageHeader
{
    private readonly AgentBackends _backends;
    private readonly AgentPreferences _preferences;
    private readonly ScanPreferences _scan;
    private readonly McpBridge _bridge;
    private readonly AgentTranscriptStore _transcripts;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IShellLauncher _shell;
    private readonly ILogger<ChatViewModel> _logger;
    private readonly IAppNavigator _navigator;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(CancelCommand), nameof(RetryCommand), nameof(RewindToCommand))]
    private bool _isBusy;

    public ChatViewModel(
        AgentBackends backends,
        AgentPreferences preferences,
        ScanPreferences scan,
        AgentModelSelector agentModel,
        McpPreferences mcp,
        McpServerHost mcpServer,
        McpBridge bridge,
        ChatHistoryStore history,
        AgentTranscriptStore transcripts,
        IDialogService dialogs,
        IClipboardService clipboard,
        IShellLauncher shell,
        IUiDispatcher uiDispatcher,
        ILogger<ChatViewModel> logger,
        IAppNavigator navigator)
    {
        _backends = backends;
        _preferences = preferences;
        _scan = scan;
        _bridge = bridge;
        _transcripts = transcripts;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _shell = shell;
        _logger = logger;
        _navigator = navigator;

        History = new(
            history,
            dialogs,
            logger,
            backends,
            preferences,
            Messages,
            () => IsBusy,
            RefreshLastFlags,
            () => OnPropertyChanged(nameof(HasMessages)));

        Gates = new(
            backends,
            preferences,
            agentModel,
            mcp,
            mcpServer,
            bridge,
            uiDispatcher,
            logger,
            () => _cts?.Cancel(),
            () => History.DropSession(IsBusy),
            () =>
            {
                _cts?.Cancel();
                History.DropSession(IsBusy);
            });
        Gates.PropertyChanged += OnGatesPropertyChanged;
        Gates.NavigationRequested += OnNavigationRequested;
    }

    public event Action? FocusRequested;

    public static IReadOnlyList<ChatExample> Examples { get; } =
    [
        new("Куда делось место на диске C?", PackIconLucideKind.HardDrive),
        new("Что тут можно снести без последствий?", PackIconLucideKind.Trash2),
        new("Сколько места занял Docker и сколько из него вернётся?", PackIconLucideKind.Container),
        new("Почему эти папки опять расходятся после синхронизации?", PackIconLucideKind.FolderSync),
    ];

    public ChatGatesViewModel Gates { get; }

    public ChatHistoryViewModel History { get; }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public string PageTitle => "Чат";

    public string? PageDescription => $"{AgentPersona.Name} – агент внутри программы: смотрит на неё теми же инструментами, что и MCP-сервер.";

    public string AgentName => AgentPersona.Name;

    public string Greeting => AgentPersona.Greeting;

    public string InputPlaceholder => AgentPersona.InputPlaceholder;

    public bool HasMessages => Messages.Count > 0;

    private IAgentBackend Backend => _backends.Current;

    private bool CanSend => !IsBusy && Gates.CanChat && !string.IsNullOrWhiteSpace(InputText);

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
        History.LoadHistory();
        await Gates.EnsureDetectedAsync();
    }

    private void OnGatesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatGatesViewModel.CliInfo) or nameof(ChatGatesViewModel.IsDetectingCli) or nameof(ChatGatesViewModel.CanChat))
        {
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnNavigationRequested(string sectionKey)
    {
        _navigator.TryNavigate(sectionKey);
    }

    [RelayCommand]
    private void UseExample(string? example)
    {
        InputText = example ?? string.Empty;
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

        _clipboard.TrySetText(message.Text);
    }

    [RelayCommand]
    private void OpenTranscript(ChatMessageViewModel? message)
    {
        if (message?.TranscriptPath is not { Length: > 0 } path)
        {
            return;
        }

        _shell.Reveal(File.Exists(path) ? path : _transcripts.DirectoryPath);
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private async Task RetryAsync(ChatMessageViewModel? message)
    {
        if (message is null || !Gates.CanChat)
        {
            return;
        }

        var index = Messages.IndexOf(message);

        if (index < 0)
        {
            return;
        }

        var question = PreviousQuestion(index);

        if (question < 0)
        {
            return;
        }

        var prompt = Messages[question].Text;

        TruncateFrom(question);

        await RunTurnAsync(prompt);
    }

    [RelayCommand(CanExecute = nameof(CanManageConversation))]
    private void RewindTo(ChatMessageViewModel? message)
    {
        if (message is not { IsUser: true })
        {
            return;
        }

        var index = Messages.IndexOf(message);

        if (index < 0)
        {
            return;
        }

        var removed = Messages.Count - index;
        var question = $"Всё, что после этого вопроса, пропадёт из разговора: сообщений – {removed}. Текст вопроса вернётся в поле ввода, а разговор с CLI начнётся заново – прежних ответов {AgentPersona.NameGenitive} он уже не вспомнит.";

        if (!_dialogs.Confirm("Вернуться к вопросу", question))
        {
            return;
        }

        InputText = message.Text;
        TruncateFrom(index);

        History.SessionId = null;
        History.ResumedFromDisk = false;

        PersistRewind();
        _logger.ChatRewound(removed);
        FocusRequested?.Invoke();
    }

    private int PreviousQuestion(int index)
    {
        for (var candidate = index - 1; candidate >= 0; candidate--)
        {
            if (Messages[candidate].IsUser)
            {
                return candidate;
            }
        }

        return -1;
    }

    private void TruncateFrom(int index)
    {
        while (Messages.Count > index)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        RefreshLastFlags();
        OnPropertyChanged(nameof(HasMessages));
    }

    private void RefreshLastFlags()
    {
        for (var index = 0; index < Messages.Count; index++)
        {
            Messages[index].IsLast = index == Messages.Count - 1;
        }
    }

    private void PersistRewind()
    {
        if (Messages.Count > 0)
        {
            History.CaptureTurn();

            return;
        }

        History.DiscardEmptyConversation();
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
        var turn = PrepareTurn(prompt);

        try
        {
            await foreach (var turnEvent in turn.Backend.RunAsync(turn.Request, turn.Token).WithCancellation(turn.Token))
            {
                ApplyTurnEvent(turn.Assistant, turnEvent);
            }
        }
        catch (OperationCanceledException)
        {
            turn.Assistant.IsCancelled = true;
        }
        catch (Exception exception)
        {
            turn.Assistant.IsError = true;
            turn.Assistant.Append(turn.Assistant.Text.Length > 0 ? $"\n\n{exception.Message}" : exception.Message);
        }
        finally
        {
            FinishTurn(turn);
        }

        if (turn.ResumedFromDisk)
        {
            History.ResumedFromDisk = false;

            if (turn.Assistant.IsError)
            {
                History.SessionId = null;
                _logger.ChatRestoredSessionDropped();
            }
        }

        History.CaptureTurn();
    }

    private TurnContext PrepareTurn(string prompt)
    {
        Messages.Add(new(ChatRole.User, prompt));

        var assistant = new ChatMessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Messages.Add(assistant);
        RefreshLastFlags();
        OnPropertyChanged(nameof(HasMessages));

        var backend = Backend;
        var mutations = Gates.Mcp.AllowMutations;
        var allowed = AgentPrompt.AllowedTools(mutations, _scan.DuplicatesEnabled);
        var transcript = _transcripts.Begin(backend.Kind, Gates.Mcp.Token);
        assistant.TranscriptPath = transcript?.Path;

        if (mutations)
        {
            _logger.AgentMutationsGranted(string.Join(", ", AgentPrompt.Destructive));
        }

        var request = new AgentRequest
        {
            Prompt = prompt,
            Context = _bridge.DescribeContext(),
            ResumeSessionId = History.SessionId,
            SystemPrompt = AgentPrompt.Build(mutations, backend.HasBuiltInShell),
            Model = _preferences.ModelFor(backend.Kind) is { Length: > 0 } model ? model : null,
            Effort = _preferences.EffortFor(backend.Kind) is { Length: > 0 } effort ? effort : null,
            Mcp = new(AgentPrompt.ServerName, Gates.McpServer.Endpoint ?? string.Empty, Gates.Mcp.Token, allowed, AgentPrompt.DeniedTools(mutations)),
            Transcript = transcript,
        };

        _cts = new();
        var resumedFromDisk = History.ResumedFromDisk && History.SessionId is { Length: > 0 };

        History.SessionDropped = false;
        IsBusy = true;
        _bridge.DeferNavigation = true;

        return new(assistant, backend, request, _cts.Token, transcript, resumedFromDisk);
    }

    private void ApplyTurnEvent(ChatMessageViewModel assistant, AgentEvent turnEvent)
    {
        switch (turnEvent.Kind)
        {
            case AgentEventKind.Started:
                if (!History.SessionDropped)
                {
                    History.SessionId = turnEvent.SessionId;
                }

                break;

            case AgentEventKind.Text:
                assistant.Append(turnEvent.Text);
                break;

            case AgentEventKind.ToolCall:
                assistant.DropPreamble();
                assistant.ToolCalls.Add(ChatToolCall.From(turnEvent));
                break;

            case AgentEventKind.Completed:
                if (!History.SessionDropped)
                {
                    History.SessionId = turnEvent.SessionId ?? History.SessionId;
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

    private void FinishTurn(TurnContext turn)
    {
        turn.Assistant.IsStreaming = false;
        IsBusy = false;
        _bridge.DeferNavigation = false;
        _cts?.Dispose();
        _cts = null;

        turn.Transcript?.Write(AgentTranscriptKind.Message, turn.Assistant.Text);
        turn.Transcript?.Dispose();
    }

    partial void OnIsBusyChanged(bool value)
    {
        History.NotifyBusyChanged();
    }

    private sealed record TurnContext(
        ChatMessageViewModel Assistant,
        IAgentBackend Backend,
        AgentRequest Request,
        CancellationToken Token,
        IAgentTranscript? Transcript,
        bool ResumedFromDisk);
}
