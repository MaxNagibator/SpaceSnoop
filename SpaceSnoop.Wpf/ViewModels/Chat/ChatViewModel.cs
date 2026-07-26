using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatViewModel : ObservableObject, IPageHeader
{
    private readonly IAgentBackend _backend;
    private readonly AgentPreferences _preferences;
    private readonly ILogger<ChatViewModel> _logger;

    private bool _detectStarted;
    private string? _sessionId;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCliMissingBanner), nameof(ShowConsentBanner), nameof(ShowMcpBanner), nameof(CanChat))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private AgentCliInfo? _cliInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCliMissingBanner))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isDetectingCli;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(CancelCommand), nameof(NewConversationCommand))]
    private bool _isBusy;

    public ChatViewModel(IAgentBackend backend, AgentPreferences preferences, McpPreferences mcp, McpServerHost mcpServer, ILogger<ChatViewModel> logger)
    {
        _backend = backend;
        _preferences = preferences;
        Mcp = mcp;
        McpServer = mcpServer;
        _logger = logger;

        _preferences.PropertyChanged += OnGateSourceChanged;
        Mcp.PropertyChanged += OnGateSourceChanged;
        McpServer.PropertyChanged += OnGateSourceChanged;
    }

    public static IReadOnlyList<string> ExampleQuestions { get; } =
    [
        "Куда делось место на диске C?",
        "Почему эти папки опять расходятся после синхронизации?",
        "Какие профили синхронизации у меня настроены?",
    ];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

    public string PageTitle => "Чат";

    public string? PageDescription => "Спроси агента про место на диске или про то, почему синхронизация не сходится – он смотрит на приложение теми же инструментами, что и MCP-сервер.";

    public bool HasMessages => Messages.Count > 0;

    public bool ShowCliMissingBanner => !IsDetectingCli && CliInfo is null;

    public bool ShowConsentBanner => !IsDetectingCli && CliInfo is not null && !_preferences.Consent;

    public bool ShowMcpBanner => !IsDetectingCli && CliInfo is not null && _preferences.Consent && !McpReady;

    public bool CanChat => !IsDetectingCli && CliInfo is not null && _preferences.Consent && McpReady;

    public bool MutationsAllowed => Mcp.AllowMutations;

    public string EmptyStateHint => MutationsAllowed
        ? "Агент смотрит на приложение теми же инструментами, что и MCP-сервер: сканирует, сравнивает, читает открытое сравнение. Изменяющие операции разрешены – синхронизацию он может запустить сам, но только показав план и дождавшись вашего согласия."
        : "Агент смотрит на приложение теми же read-only инструментами, что и MCP-сервер: сканирует, сравнивает, читает открытое сравнение. Ничего не удаляет и не переносит сам.";

    private bool McpReady => Mcp.Enabled && McpServer.IsRunning && !string.IsNullOrWhiteSpace(Mcp.Token);

    private bool CanSend => !IsBusy && CanChat && !string.IsNullOrWhiteSpace(InputText);

    private bool CanCancel => IsBusy;

    private bool CanManageConversation => !IsBusy;

    public async Task EnsureLoadedAsync()
    {
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
        IsDetectingCli = true;

        try
        {
            CliInfo = await Task.Run(_backend.Detect);
        }
        finally
        {
            IsDetectingCli = false;
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
        OnPropertyChanged(nameof(HasMessages));
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
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
        Messages.Add(new ChatMessageViewModel(ChatRole.User, prompt));

        var assistant = new ChatMessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Messages.Add(assistant);
        OnPropertyChanged(nameof(HasMessages));

        var mutations = Mcp.AllowMutations;
        var allowed = AgentPrompt.AllowedTools(mutations);

        if (mutations)
        {
            _logger.AgentMutationsGranted(string.Join(", ", AgentPrompt.Destructive));
        }

        var request = new AgentRequest
        {
            Prompt = prompt,
            ResumeSessionId = _sessionId,
            SystemPrompt = AgentPrompt.Build(mutations),
            Model = string.IsNullOrWhiteSpace(_preferences.Model) ? null : _preferences.Model,
            Mcp = new AgentMcpConfig(AgentPrompt.ServerName, McpServer.Endpoint ?? string.Empty, Mcp.Token, allowed, AgentPrompt.DeniedTools(mutations)),
        };

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;

        try
        {
            await foreach (var turnEvent in _backend.RunAsync(request, token).WithCancellation(token))
            {
                switch (turnEvent.Kind)
                {
                    case AgentEventKind.Started:
                        _sessionId = turnEvent.SessionId;
                        break;

                    case AgentEventKind.Text:
                        assistant.Append(turnEvent.Text);
                        break;

                    case AgentEventKind.ToolCall:
                        assistant.ToolCalls.Add(ChatToolCall.From(turnEvent.ToolName));
                        break;

                    case AgentEventKind.Completed:
                        _sessionId = turnEvent.SessionId ?? _sessionId;
                        assistant.CostUsd = turnEvent.CostUsd;
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

        // TODO: история разговора живёт только в памяти VM-синглтона – переживает навигацию между
        // страницами, но не перезапуск приложения. Персистентность (например, тот же settings.toml
        // или отдельный файл) – когда появится первый реальный запрос сохранять историю чата.
    }

    private void OnGateSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _preferences) && e.PropertyName == nameof(AgentPreferences.Enabled) && !_preferences.Enabled)
        {
            _cts?.Cancel();
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(NotifyGatesChanged);
            return;
        }

        NotifyGatesChanged();
    }

    private void NotifyGatesChanged()
    {
        OnPropertyChanged(nameof(ShowConsentBanner));
        OnPropertyChanged(nameof(ShowMcpBanner));
        OnPropertyChanged(nameof(CanChat));
        OnPropertyChanged(nameof(MutationsAllowed));
        OnPropertyChanged(nameof(EmptyStateHint));
        SendCommand.NotifyCanExecuteChanged();
    }
}
