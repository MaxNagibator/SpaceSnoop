using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Net;

namespace SpaceSnoop.Wpf.Mcp;

public sealed partial class McpServerHost : ObservableObject, IDisposable
{
    private readonly McpPreferences _preferences;
    private readonly McpBridge _bridge;
    private readonly ILogger<McpServerHost> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _app;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private string? _endpoint;

    [ObservableProperty]
    private string? _lastError;

    public McpServerHost(McpPreferences preferences, McpBridge bridge, ILogger<McpServerHost> logger)
    {
        _preferences = preferences;
        _bridge = bridge;
        _logger = logger;
        _preferences.PropertyChanged += OnPreferencesChanged;
    }

    public bool IsRunning => Endpoint is not null;

    public void Apply()
    {
        if (_disposed)
        {
            return;
        }

        _ = ApplyAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _preferences.PropertyChanged -= OnPreferencesChanged;

        try
        {
            if (!Task.Run(StopAsync).Wait(TimeSpan.FromSeconds(AppDefaults.McpShutdownTimeoutSeconds)))
            {
                _logger.McpServerStopTimedOut(AppDefaults.McpShutdownTimeoutSeconds);
            }
        }
        catch (Exception exception)
        {
            _logger.McpServerStopFailed(exception.Unwrap());
        }
    }

    private Task ApplyAsync()
    {
        return Task.Run(ApplyCoreAsync);
    }

    private async Task ApplyCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            if (!_preferences.Enabled)
            {
                LastError = null;
                return;
            }

            var token = _preferences.Token.Trim();

            if (token.Length == 0)
            {
                LastError = "Сервер не запущен: задайте токен доступа.";
                _logger.McpServerTokenMissing();
                return;
            }

            await StartCoreAsync(_preferences.Port, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            _logger.McpServerFailed(exception, _preferences.Port);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartCoreAsync(int port, string token)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

        builder.Services.AddSingleton(_bridge);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<SpaceSnoopTools>();

        var app = builder.Build();
        app.Use(async (context, next) => await AuthorizeAsync(context, next, token).ConfigureAwait(false));
        app.MapMcp(AppDefaults.McpEndpointPath);

        await app.StartAsync().ConfigureAwait(false);

        _app = app;
        Endpoint = $"http://127.0.0.1:{port}{AppDefaults.McpEndpointPath}";
        LastError = null;
        _logger.McpServerStarted(Endpoint);

        if (AdminElevation.IsElevated)
        {
            _logger.McpServerElevated(Endpoint);
        }
    }

    private static async Task AuthorizeAsync(HttpContext context, RequestDelegate next, string token)
    {
        if (!McpAuth.IsOriginAllowed(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("SpaceSnoop MCP: запрос с чужого источника", context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!IsAuthorized(context, token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("SpaceSnoop MCP: требуется токен доступа", context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool IsAuthorized(HttpContext context, string token)
    {
        return McpAuth.IsAuthorized(
            context.Request.Headers.Authorization.ToString(),
            context.Request.Headers[McpAuth.TokenHeader].ToString(),
            token);
    }

    private async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _logger.McpServerStopped();
        }
        finally
        {
            _app = null;
            Endpoint = null;
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(McpPreferences.Enabled) or nameof(McpPreferences.Port) or nameof(McpPreferences.Token))
        {
            Apply();
        }
    }
}
