using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed partial class ChatGatesViewModel : ObservableObject
{
    private readonly AgentBackends _backends;
    private readonly AgentPreferences _preferences;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger _logger;
    private readonly Action _cancelActiveTurn;
    private readonly Action _dropSessionIfBusy;
    private readonly Action _beginBackendSwitch;

    private bool _detectStarted;
    private int _detectGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCliMissingBanner), nameof(ShowConsentBanner), nameof(ShowMcpBanner), nameof(CanChat), nameof(ShowShellBanner))]
    private AgentCliInfo? _cliInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCliMissingBanner), nameof(ShowShellBanner))]
    private bool _isDetectingCli;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CliMissingTitle), nameof(CliMissingText), nameof(HasAlternativeCli), nameof(AlternativeCliText), nameof(SwitchToAlternativeText), nameof(ShowInstallHints))]
    private AgentBackendChoice? _backendChoice;

    [ObservableProperty]
    private ChatPendingNavigation? _pendingNavigation;

    internal ChatGatesViewModel(
        AgentBackends backends,
        AgentPreferences preferences,
        AgentModelSelector agentModel,
        McpPreferences mcp,
        McpServerHost mcpServer,
        McpBridge bridge,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Action cancelActiveTurn,
        Action dropSessionIfBusy,
        Action beginBackendSwitch)
    {
        _backends = backends;
        _preferences = preferences;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _cancelActiveTurn = cancelActiveTurn;
        _dropSessionIfBusy = dropSessionIfBusy;
        _beginBackendSwitch = beginBackendSwitch;
        AgentModel = agentModel;
        Mcp = mcp;
        McpServer = mcpServer;
        BackendOptions = [.. backends.All.Select(item => new AgentBackendOption(item.Kind, item.DisplayName, DescribeBackend(item)))];

        _preferences.PropertyChanged += OnGateSourceChanged;
        Mcp.PropertyChanged += OnGateSourceChanged;
        McpServer.PropertyChanged += OnGateSourceChanged;
        bridge.NavigationDeferred += OnNavigationDeferred;
    }

    public event Action<string>? NavigationRequested;

    public AgentModelSelector AgentModel { get; }

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

    public bool ShowCliMissingBanner => !IsDetectingCli && CliInfo is null;

    public bool ShowConsentBanner => !IsDetectingCli && CliInfo is not null && !_preferences.Consent;

    public bool ShowMcpBanner => !IsDetectingCli && CliInfo is not null && _preferences.Consent && !McpReady;

    public bool CanChat => !IsDetectingCli && CliInfo is not null && _preferences.Consent && McpReady;

    public bool MutationsAllowed => Mcp.AllowMutations;

    public string BackendName => Backend.DisplayName;

    public string DetectingCliText => $"Проверяю, установлен ли {Backend.DisplayName}…";

    public string CliMissingTitle => BackendChoice is { NothingFound: true }
        ? "CLI агента не найден"
        : $"{Backend.DisplayName} не найден";

    public string CliMissingText => BackendChoice is { NothingFound: true }
        ? "Чат работает через уже установленный на этой машине CLI агента – по вашей подписке, без ключей в настройках. Приложение поддерживает три таких CLI и не нашло ни одного. Поставьте любой из них, войдите в свою подписку и проверьте снова:"
        : $"Чат работает через уже установленный на этой машине CLI {Backend.DisplayName} – по вашей подписке, без ключей в настройках. Установите CLI и войдите в свою подписку, затем проверьте снова: {Backend.MissingCliHint}";

    public bool ShowInstallHints => BackendChoice is { NothingFound: true };

    public string InstallHintsText => string.Join(Environment.NewLine, _backends.All.Select(item => $"{item.DisplayName}: {item.MissingCliHint}"));

    public bool HasAlternativeCli => SuggestedBackend is not null;

    public string AlternativeCliText => SuggestedBackend is { } suggested
        ? $"На этой машине найден {suggested.DisplayName}. Переключение сменит поставщика модели и начнёт разговор заново.{DescribeShell(suggested)}"
        : string.Empty;

    public string SwitchToAlternativeText => SuggestedBackend is { } suggested ? $"Переключиться на {suggested.DisplayName}" : string.Empty;

    public IReadOnlyList<AgentBackendOption> BackendOptions { get; }

    public AgentBackendOption SelectedBackendOption
    {
        get => BackendOptions.FirstOrDefault(option => option.Kind == _preferences.Backend) ?? BackendOptions[0];

        set
        {
            if (value is null || value.Kind == _preferences.Backend)
            {
                return;
            }

            _preferences.Backend = value.Kind;
        }
    }

    public string ConsentText => $"Текст сообщения и то, что агент запрашивает у инструментов приложения (пути, размеры, результаты сравнения), уходит в CLI {Backend.DisplayName} и дальше поставщику модели – под вашей собственной подпиской, не по ключу приложения.";

    public bool ShowShellBanner => Backend.HasBuiltInShell && CanChat;

    public string ShellNotice => $"У CLI {Backend.DisplayName} есть собственная оболочка операционной системы, и отключить её нечем: помимо инструментов приложения {AgentPersona.Name} может выполнять команды с правами SpaceSnoop. Запуск команды виден в ленте отдельным бейджем.";

    public string ShellNoticeShort => $"{Backend.DisplayName}: у {AgentPersona.NameGenitive} есть оболочка системы – он может выполнять команды с правами SpaceSnoop";

    public string MutationsNoticeShort => $"Изменяющие операции разрешены: {AgentPersona.Name} может сам запустить синхронизацию, упаковать каталог в архив и пометить лишнее на удаление";

    public string MutationsNotice => $"Изменяющие операции разрешены в настройках: {AgentPersona.Name} может сам запустить синхронизацию (файлы скопируются, лишние уйдут в корзину) и упаковать каталог в архив. Сначала он обязан показать план и дождаться вашего согласия. Пометки на удаление он тоже ставит сам, но удаляет помеченное только человек.";

    public string EmptyStateHint => MutationsAllowed ? AgentPersona.MutationsNote : AgentPersona.ReadOnlyNote;

    private IAgentBackend Backend => _backends.Current;

    private IAgentBackend? SuggestedBackend => BackendChoice?.Suggested is { } kind
        ? _backends.All.FirstOrDefault(item => item.Kind == kind)
        : null;

    private bool McpReady => Mcp.Enabled && McpServer.IsRunning && !string.IsNullOrWhiteSpace(Mcp.Token);

    internal async Task EnsureDetectedAsync()
    {
        if (_detectStarted)
        {
            return;
        }

        _detectStarted = true;
        await DetectCliAsync();
    }

    private void OnNavigationDeferred(string sectionKey)
    {
        if (ChatPendingNavigation.For(sectionKey) is not { } pending)
        {
            return;
        }

        PendingNavigation = pending;
        _logger.AgentNavigationDeferred(pending.Page);
    }

    private void OnGateSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _preferences))
        {
            if (e.PropertyName == nameof(AgentPreferences.Enabled) && !_preferences.Enabled)
            {
                _cancelActiveTurn();
            }

            if (e.PropertyName == nameof(AgentPreferences.Backend))
            {
                SwitchBackend();
                return;
            }
        }

        if (ReferenceEquals(sender, Mcp) && e.PropertyName == nameof(McpPreferences.AllowMutations) && !Backend.SendsSystemPromptEachTurn)
        {
            _dropSessionIfBusy();
        }

        _uiDispatcher.Invoke(NotifyGatesChanged);
    }

    [RelayCommand]
    private async Task RecheckCliAsync()
    {
        foreach (var item in _backends.All)
        {
            item.InvalidateDetection();
        }

        await DetectCliAsync();
    }

    [RelayCommand]
    private void SwitchToAlternative()
    {
        if (BackendChoice?.Suggested is not { } suggested)
        {
            return;
        }

        _preferences.Backend = suggested;
    }

    [RelayCommand]
    private void OpenPendingPage()
    {
        if (PendingNavigation is not { } pending)
        {
            return;
        }

        PendingNavigation = null;
        NavigationRequested?.Invoke(pending.Key);
    }

    [RelayCommand]
    private void DismissPendingPage()
    {
        PendingNavigation = null;
    }

    private async Task DetectCliAsync()
    {
        var backend = Backend;
        var generation = ++_detectGeneration;

        IsDetectingCli = true;

        try
        {
            var (info, choice) = await ProbeAsync(backend);

            if (generation == _detectGeneration)
            {
                CliInfo = info;
                BackendChoice = choice;
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

    private async Task<(AgentCliInfo? Cli, AgentBackendChoice? Choice)> ProbeAsync(IAgentBackend backend)
    {
        var cli = await Task.Run(() => Detect(backend), CancellationToken.None);

        if (cli is not null)
        {
            return (cli, null);
        }

        var others = _backends.All
            .Where(item => item.Kind != backend.Kind)
            .Select(item => Task.Run(() => new AgentBackendProbe(item.Kind, Detect(item)), CancellationToken.None));

        var probes = await Task.WhenAll(others);

        return (null, AgentBackendChoice.From(backend.Kind, [new(backend.Kind, null), .. probes]));
    }

    private AgentCliInfo? Detect(IAgentBackend backend)
    {
        try
        {
            return backend.Detect();
        }
        catch (Exception exception)
        {
            _logger.AgentCliDetectionFailed(exception, backend.DisplayName);
            return null;
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

    private void SwitchBackend()
    {
        _beginBackendSwitch();
        _logger.AgentBackendChanged(Backend.DisplayName);

        _uiDispatcher.Invoke(ReloadBackend);
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
        OnPropertyChanged(nameof(SelectedBackendOption));
    }

    private static string DescribeShell(IAgentBackend backend)
    {
        return backend.HasBuiltInShell
            ? $" У CLI {backend.DisplayName} есть собственная оболочка операционной системы, и отключить её нечем: {AgentPersona.Name} может выполнять команды с правами SpaceSnoop."
            : string.Empty;
    }

    private void NotifyGatesChanged()
    {
        OnPropertyChanged(nameof(ShowConsentBanner));
        OnPropertyChanged(nameof(ShowMcpBanner));
        OnPropertyChanged(nameof(CanChat));
        OnPropertyChanged(nameof(ShowShellBanner));
        OnPropertyChanged(nameof(MutationsAllowed));
        OnPropertyChanged(nameof(EmptyStateHint));
    }

    private static string DescribeBackend(IAgentBackend backend)
    {
        return $"CLI {backend.CliName}. Переключение начнёт разговор заново: идентификатор сессии выдаёт сам CLI, и другому он не подходит.{DescribeShell(backend)}";
    }
}
