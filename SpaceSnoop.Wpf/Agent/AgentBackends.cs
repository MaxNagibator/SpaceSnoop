namespace SpaceSnoop.Wpf.Agent;

using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class AgentBackends : IDisposable
{
    private readonly AgentPreferences _preferences;
    private readonly string _workspaceRoot;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _gate = new();
    private readonly Dictionary<AgentBackendKind, Entry> _entries = [];

    public IReadOnlyList<IAgentBackend> All => [.. AgentBackendChoice.Order.Select(Get)];

    public IAgentBackend Current => Get(_preferences.Backend);

    public AgentBackends(AgentPreferences preferences, ILoggerFactory loggerFactory)
        : this(preferences, Path.Combine(AppStorage.DataDirectory, "agents"), loggerFactory)
    {
    }

    internal AgentBackends(AgentPreferences preferences, string workspaceRoot, ILoggerFactory? loggerFactory = null)
    {
        _preferences = preferences;
        _workspaceRoot = workspaceRoot;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public void PrepareWorkspace(AgentBackendKind kind)
    {
        kind = Normalize(kind);
        var directory = Directory.CreateDirectory(Workspace(kind));
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Рабочий каталог агента не может быть ссылкой.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Не определён пользователь агента.");
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);

        if (kind == AgentBackendKind.Antigravity)
        {
            // TODO: убрать очистку после поставки пакета, который сам восстанавливает осиротевший MCP-конфиг и отвергает ссылки.
            var configDirectory = Path.Combine(directory.FullName, ".agents");
            if (Path.Exists(configDirectory))
            {
                if ((File.GetAttributes(configDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Каталог MCP агента не может быть ссылкой.");
                }

                File.Delete(Path.Combine(configDirectory, "mcp_config.json"));
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                (entry.Backend as IDisposable)?.Dispose();
            }

            _entries.Clear();
        }
    }

    private IAgentBackend Get(AgentBackendKind kind)
    {
        kind = Normalize(kind);

        lock (_gate)
        {
            var path = _preferences.CliPathFor(kind);
            if (_entries.TryGetValue(kind, out var entry))
            {
                if (entry.CliPath == path)
                {
                    return entry.Backend;
                }

                (entry.Backend as IDisposable)?.Dispose();
            }

            var options = new AgentExecutionOptions
            {
                ExecutablePath = path,
                WorkingDirectory = Workspace(kind),
                Timeout = TimeSpan.FromMinutes(2),
            };
            IAgentBackend backend = kind switch
            {
                AgentBackendKind.Claude => new ClaudeAgentBackend(options, _loggerFactory.CreateLogger<ClaudeAgentBackend>()),
                AgentBackendKind.Codex => new CodexAgentBackend(options, _loggerFactory.CreateLogger<CodexAgentBackend>()),
                AgentBackendKind.OpenCode => new OpenCodeAgentBackend(options, _loggerFactory.CreateLogger<OpenCodeAgentBackend>()),
                AgentBackendKind.Antigravity => new AntigravityAgentBackend(options),
                _ => throw new InvalidOperationException("Неизвестный агент."),
            };
            _entries[kind] = new(path, backend);
            return backend;
        }
    }

    private string Workspace(AgentBackendKind kind) => Path.Combine(_workspaceRoot, kind.ToString().ToLowerInvariant());

    private static AgentBackendKind Normalize(AgentBackendKind kind) => AgentBackendChoice.Order.Contains(kind)
        ? kind : AppDefaults.AgentBackendDefault;

    private sealed record Entry(string CliPath, IAgentBackend Backend);
}
