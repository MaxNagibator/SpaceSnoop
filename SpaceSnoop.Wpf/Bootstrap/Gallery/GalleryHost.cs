using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Views;

namespace SpaceSnoop.Wpf.Bootstrap.Gallery;

/// <summary>
/// SpaceSnoop глазами каркасной галереи: список кейсов и съёмка одного кадра. Цикл по темам, окно за
/// экраном, пропуски, индекс и код возврата держит <see cref="GalleryRunner" />.
/// </summary>
internal sealed class GalleryHost : IGalleryHost
{
    public const string DialogKind = "dialog";
    public const string TipKind = "tip";

    private const string DialogFilePrefix = "dialog-";
    private const string TipFilePrefix = "tip-";
    private const string StateTag = "state";

    private readonly IServiceProvider _services;
    private readonly GalleryFixture _fixture;
    private readonly GalleryOptions _options;
    private readonly ShellViewModel _shell;
    private readonly Window _window;

    private GalleryHost(IServiceProvider services, GalleryFixture fixture, GalleryOptions options, IReadOnlyList<GalleryCase> cases)
    {
        _services = services;
        _fixture = fixture;
        _options = options;
        _shell = services.GetRequiredService<ShellViewModel>();
        _window = services.GetRequiredService<MainWindow>();

        Cases = cases;
        Tags = new Dictionary<string, string>(StringComparer.Ordinal) { [StateTag] = options.State };
    }

    public string AppName => AppInfo.Name;

    public string AppVersion => AppInfo.Version;

    public Window Window => _window;

    public IReadOnlyList<GalleryCase> Cases { get; }

    public IReadOnlyDictionary<string, string>? Tags { get; }

    /// <summary>
    /// Отбирает кейсы, которые поддержаны выбранным состоянием, и докладывает отброшенное: иначе
    /// «сняли всё» читалось бы по каталогу, где половины кейсов нет.
    /// </summary>
    public static GalleryHost Create(IServiceProvider services, GalleryFixture fixture, GalleryOptions options, ILogger logger)
    {
        var pages = options.Pages.Where(page => GalleryStates.SupportsPage(page, options.State)).ToList();
        var dialogs = options.Dialogs.Where(dialog => GalleryStates.SupportsDialog(dialog, options.State)).ToList();
        var tips = GalleryStates.SupportsTip(options.State) ? options.Tips : [];

        var dropped = options.Pages.Count - pages.Count
            + options.Dialogs.Count - dialogs.Count
            + options.Tips.Count - tips.Count;

        if (dropped > 0)
        {
            logger.GalleryStateSkipped(options.State, dropped);
        }

        List<GalleryCase> cases =
        [
            .. pages.Select(page => new GalleryCase(GalleryCase.PageKind, page)),
            .. dialogs.Select(dialog => new GalleryCase(DialogKind, dialog)),
            .. tips.Select(tip => new GalleryCase(TipKind, tip)),
        ];

        return new(services, fixture, options, cases);
    }

    public Task ArrangeAsync()
    {
        return ArrangeAsync(_services, _fixture);
    }

    /// <summary>Раскладка фикстур: те же данные, что видит человек. Зовут галерея и smoke-тесты биндингов.</summary>
    internal static async Task ArrangeAsync(IServiceProvider services, GalleryFixture fixture)
    {
        var scan = services.GetRequiredService<ScanViewModel>();
        await scan.ScanFromAutomationAsync(fixture.Left, CancellationToken.None).ConfigureAwait(true);
        scan.SelectedNode = scan.Roots.FirstOrDefault();

        var sync = services.GetRequiredService<SyncViewModel>();
        sync.Setup.LeftPath = fixture.Left;
        sync.Setup.RightPath = fixture.Right;
        await sync.Operations.CompareFromAutomationAsync(CancellationToken.None).ConfigureAwait(true);
    }

    public Task ThemeAppliedAsync(string themeKey)
    {
        _shell.IsTarkovBootPlaying = false;

        return Task.CompletedTask;
    }

    public async Task<GalleryShot> CaptureAsync(GalleryCase item, GalleryContext context)
    {
        _shell.Toasts.Toasts.Clear();

        var shot = item.Kind switch
        {
            DialogKind => await CaptureDialogAsync(item.Name, context).ConfigureAwait(true),
            TipKind => await CaptureTipAsync(item.Name, context).ConfigureAwait(true),
            _ => await CapturePageAsync(item.Name, context).ConfigureAwait(true),
        };

        return shot with { Tags = Tags };
    }

    private async Task<GalleryShot> CapturePageAsync(string page, GalleryContext context)
    {
        Navigate(page, $"Страница «{page}» не открылась – её нет в навигации.");

        try
        {
            await GalleryStates.ApplyPageAsync(page, _options.State, _services, _fixture).ConfigureAwait(true);
            await context.SettleAsync().ConfigureAwait(true);

            return context.Save(Slug(string.Empty, page));
        }
        finally
        {
            GalleryStates.ResetPage(page, _options.State, _services);
        }
    }

    private async Task<GalleryShot> CaptureDialogAsync(string dialog, GalleryContext context)
    {
        var modals = _shell.Modal;
        var section = GalleryDialogs.Section(dialog);

        Navigate(section, $"Страница «{section}» не открылась – диалог «{dialog}» негде показать.");

        await context.SettleAsync().ConfigureAwait(true);
        GalleryDialogs.Open(dialog, _services, _fixture, modals);

        try
        {
            if (!await WaitForModalAsync(modals, true).ConfigureAwait(true))
            {
                throw new InvalidOperationException($"Диалог «{dialog}» не открылся за отведённое время.");
            }

            GalleryStates.ApplyDialog(modals.Current, _options.State);
            await context.SettleAsync().ConfigureAwait(true);

            return context.Save(_window, Slug(DialogFilePrefix, dialog));
        }
        finally
        {
            GalleryStates.ResetDialog(modals.Current);
            modals.RequestCancel();
            await WaitForModalAsync(modals, false).ConfigureAwait(true);
            GalleryDialogs.Cleanup(dialog, _services);
            await context.SettleAsync().ConfigureAwait(true);
        }
    }

    private async Task<GalleryShot> CaptureTipAsync(string tip, GalleryContext context)
    {
        var section = GalleryTips.Section(tip);

        Navigate(section, $"Страница «{section}» не открылась – подсказку «{tip}» негде показать.");

        GalleryTips.Prepare(tip, _services);
        await context.SettleAsync().ConfigureAwait(true);

        // Единственное место галереи, где окно видно человеку: за экраном Popup вытолкнулся бы на
        // видимый монитор, и снятое смещение соврало бы.
        _window.Left = 0;
        _window.Top = 0;
        await context.SettleAsync().ConfigureAwait(true);

        var host = GalleryTips.Host(_window);

        var tooltip = host.ShowTooltipForAutomation(GalleryTips.Point(tip, host))
            ?? throw new InvalidOperationException($"Подсказка «{tip}» не открылась – под точкой нет плитки.");

        try
        {
            await context.SettleAsync().ConfigureAwait(true);

            return context.Save(_window, tooltip, context.OffsetTo(tooltip), Slug(TipFilePrefix, tip));
        }
        finally
        {
            host.HideTooltipForAutomation();
            _window.Left = OffScreenWindow.Position;
            _window.Top = OffScreenWindow.Position;
            await context.SettleAsync().ConfigureAwait(true);
        }
    }

    private void Navigate(string section, string failure)
    {
        if (!_shell.TryNavigate(section))
        {
            throw new InvalidOperationException(failure);
        }
    }

    private string Slug(string prefix, string name)
    {
        var state = GalleryStates.IsIdle(_options.State) ? string.Empty : $"-{_options.State}";

        return $"{prefix}{ViewCapture.Slug(name)}{state}";
    }

    private static async Task<bool> WaitForModalAsync(ModalHostViewModel modals, bool active)
    {
        for (var attempt = 0; attempt < AppDefaults.GalleryModalAttempts; attempt++)
        {
            if (modals.HasActive == active)
            {
                return true;
            }

            await Task.Delay(AppDefaults.GalleryModalPollMs).ConfigureAwait(true);
        }

        return modals.HasActive == active;
    }
}

/// <summary>Прогон докладывает о себе в общий журнал приложения (`AppLog` 2200–2205).</summary>
internal sealed class GalleryJournal(ILogger logger) : IGalleryJournal
{
    public void Started(int cases, int themes, string directory)
    {
        logger.GalleryStarted(cases, themes, directory);
    }

    public void CaseFailed(Exception exception, string caseName)
    {
        logger.GalleryCaseFailed(exception.Unwrap(), caseName);
    }

    public void Finished(int frames, int skipped, long elapsedMs)
    {
        logger.GalleryFinished(frames, skipped, elapsedMs);
    }
}
