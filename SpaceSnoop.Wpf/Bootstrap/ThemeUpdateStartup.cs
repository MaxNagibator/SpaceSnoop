using KeepShell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ThemeUpdateStartup : IDisposable
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(8);

    private readonly Func<CancellationToken, Task<ThemePackUpdateCheckResult>> _checkOnStartup;
    private readonly ToastNotifier _notifier;
    private readonly Action _openCatalog;
    private readonly Func<Task<bool>> _restartApp;
    private readonly ILogger<ThemeUpdateStartup> _logger;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _started;

    public ThemeUpdateStartup(
        ThemePackUpdateService updates,
        ToastNotifier notifier,
        ThemePacksViewModel themePacks,
        Func<Task<bool>> restartApp,
        ILogger<ThemeUpdateStartup> logger)
        : this(
            updates.CheckOnStartupAsync,
            notifier,
            () => themePacks.OpenCatalogCommand.Execute(null),
            restartApp,
            logger)
    {
    }

    internal ThemeUpdateStartup(
        Func<CancellationToken, Task<ThemePackUpdateCheckResult>> checkOnStartup,
        ToastNotifier notifier,
        Action openCatalog,
        Func<Task<bool>> restartApp,
        ILogger<ThemeUpdateStartup> logger)
    {
        _checkOnStartup = checkOnStartup;
        _notifier = notifier;
        _openCatalog = openCatalog;
        _restartApp = restartApp;
        _logger = logger;
    }

    internal static Func<Task<bool>> CreateRestartCallback(IServiceProvider provider) =>
        () => RestartApplicationAsync(
            () => provider.GetRequiredService<ShellViewModel>().RequestCloseAsync(),
            provider.GetRequiredService<ISettingsStore>(),
            provider.GetRequiredService<IShellLauncher>(),
            provider.GetRequiredService<IDialogService>(),
            provider.GetRequiredService<IApplicationLifetime>());

    internal static async Task<bool> RestartApplicationAsync(
        Func<Task<bool>> requestCloseAsync,
        ISettingsStore settings,
        IShellLauncher shell,
        IDialogService dialogs,
        IApplicationLifetime lifetime,
        Func<string?>? resolveExecutable = null)
    {
        if (!await requestCloseAsync())
        {
            return false;
        }

        settings.Flush();

        var executable = resolveExecutable is not null ? resolveExecutable() : Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable) || !shell.Start(executable))
        {
            dialogs.Error(
                "Перезапуск",
                "Не удалось перезапустить программу – закройте и откройте её сами.");
            return false;
        }

        settings.Close();
        lifetime.Shutdown();
        return true;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        var token = _disposeCts.Token;
        _ = Task.Run(() => CheckAndNotifyAsync(token), token);
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    public async Task CheckAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        ThemePackUpdateCheckResult result;

        try
        {
            result = await _checkOnStartup(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.ThemeUpdateCheckFailed(ex);
            return;
        }

        if (result.AutoUpdated.Count > 0 && result.RestartRequired)
        {
            var message = result.AutoUpdated.Count == 1
                ? $"Обновлена тема «{result.AutoUpdated[0].Name}» – применится после перезапуска"
                : $"Обновлены темы ({result.AutoUpdated.Count}) – применятся после перезапуска";

            _notifier.Notify(
                message,
                "Перезапустить",
                () => _ = TriggerRestartAsync(),
                StatusSeverity.Info,
                ToastDuration);
            return;
        }

        if (result.AvailableUpdates.Count > 0)
        {
            _notifier.Notify(
                $"Доступно обновление тем ({result.AvailableUpdates.Count})",
                "Открыть каталог",
                _openCatalog,
                StatusSeverity.Info,
                ToastDuration);
        }
    }

    private async Task TriggerRestartAsync()
    {
        try
        {
            await _restartApp();
        }
        catch (Exception ex)
        {
            _logger.ThemeUpdateRestartFailed(ex);
        }
    }
}
