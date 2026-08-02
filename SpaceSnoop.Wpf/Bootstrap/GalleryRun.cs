using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Views;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Bootstrap;

internal sealed record GalleryFrame(string Kind, string Name, string Theme, string File, int Width, int Height);

internal sealed record GalleryIndex(
    string App,
    string Version,
    int Width,
    int Height,
    double Scale,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<GalleryFrame> Frames);

public static class GalleryRun
{
    public const string FolderName = "gallery";
    public const string IndexFileName = "index.json";

    private const double OffScreen = -32000;
    private const string PageKind = "page";
    private const string DialogKind = "dialog";
    private const string TipKind = "tip";
    private const string DialogFilePrefix = "dialog-";
    private const string TipFilePrefix = "tip-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> RenderAsync(
        GalleryOptions options,
        IServiceProvider services,
        GalleryFixture fixture,
        ILogger logger)
    {
        var stopwatch = Stopwatch.StartNew();
        var window = services.GetRequiredService<MainWindow>();
        var shell = services.GetRequiredService<ShellViewModel>();
        var modals = shell.Modal;

        Directory.CreateDirectory(options.Directory);
        logger.GalleryStarted(options.Pages.Count + options.Dialogs.Count + options.Tips.Count, options.Themes.Count, options.Directory);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = OffScreen;
        window.Top = OffScreen;
        window.Width = options.Width;
        window.Height = options.Height;
        window.ShowInTaskbar = false;
        window.Show();

        await ArrangeAsync(services, fixture).ConfigureAwait(true);

        var frames = new List<GalleryFrame>();
        var skipped = 0;

        foreach (var theme in options.Themes)
        {
            var themeKey = AppThemes.ToKey(theme);
            ThemeManager.Apply(themeKey);
            shell.IsTarkovBootPlaying = false;
            await Task.Delay(AppDefaults.GalleryThemeDelayMs).ConfigureAwait(true);

            foreach (var page in options.Pages)
            {
                try
                {
                    shell.Toasts.Toasts.Clear();
                    frames.Add(await CaptureAsync(window, shell, options, page, themeKey).ConfigureAwait(true));
                }
                catch (Exception exception)
                {
                    skipped++;
                    logger.GalleryCaseFailed(exception.Unwrap(), $"{page}/{themeKey}");
                }
            }

            foreach (var dialog in options.Dialogs)
            {
                try
                {
                    shell.Toasts.Toasts.Clear();
                    frames.Add(await CaptureDialogAsync(window, shell, modals, services, fixture, options, dialog, themeKey).ConfigureAwait(true));
                }
                catch (Exception exception)
                {
                    skipped++;
                    logger.GalleryCaseFailed(exception.Unwrap(), $"{dialog}/{themeKey}");
                }
            }

            foreach (var tip in options.Tips)
            {
                try
                {
                    shell.Toasts.Toasts.Clear();
                    frames.Add(await CaptureTipAsync(window, shell, services, options, tip, themeKey).ConfigureAwait(true));
                }
                catch (Exception exception)
                {
                    skipped++;
                    logger.GalleryCaseFailed(exception.Unwrap(), $"{tip}/{themeKey}");
                }
            }
        }

        WriteIndex(options, frames);

        window.Hide();
        logger.GalleryFinished(frames.Count, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);

        return skipped == 0 && frames.Count > 0 ? 0 : 1;
    }

    private static async Task<GalleryFrame> CaptureAsync(
        Window window,
        ShellViewModel shell,
        GalleryOptions options,
        string page,
        string themeKey)
    {
        if (!shell.TryNavigate(page))
        {
            throw new InvalidOperationException($"Страница «{page}» не открылась – её нет в навигации.");
        }

        await SettleAsync(window).ConfigureAwait(true);

        var file = $"{ViewCapture.Slug(page)}-{themeKey}{ViewCapture.FileExtension}";
        var (width, height) = ViewCapture.Save(Target(window, options), Path.Combine(options.Directory, file), options.Scale);

        if (width == 0 || height == 0)
        {
            throw new InvalidOperationException("Окно не отрисовано – нулевой размер кадра.");
        }

        return new(PageKind, page, themeKey, file, width, height);
    }

    private static async Task<GalleryFrame> CaptureDialogAsync(
        Window window,
        ShellViewModel shell,
        ModalHostViewModel modals,
        IServiceProvider services,
        GalleryFixture fixture,
        GalleryOptions options,
        string dialog,
        string themeKey)
    {
        var section = GalleryDialogs.Section(dialog);

        if (!shell.TryNavigate(section))
        {
            throw new InvalidOperationException($"Страница «{section}» не открылась – диалог «{dialog}» негде показать.");
        }

        await SettleAsync(window).ConfigureAwait(true);
        GalleryDialogs.Open(dialog, services, fixture, modals);

        try
        {
            if (!await WaitForModalAsync(modals, true).ConfigureAwait(true))
            {
                throw new InvalidOperationException($"Диалог «{dialog}» не открылся за отведённое время.");
            }

            await SettleAsync(window).ConfigureAwait(true);

            var file = $"{DialogFilePrefix}{ViewCapture.Slug(dialog)}-{themeKey}{ViewCapture.FileExtension}";
            var (width, height) = ViewCapture.Save(window, Path.Combine(options.Directory, file), options.Scale);

            if (width == 0 || height == 0)
            {
                throw new InvalidOperationException("Окно не отрисовано – нулевой размер кадра.");
            }

            return new(DialogKind, dialog, themeKey, file, width, height);
        }
        finally
        {
            modals.RequestCancel();
            await WaitForModalAsync(modals, false).ConfigureAwait(true);
            GalleryDialogs.Cleanup(dialog, services);
            await SettleAsync(window).ConfigureAwait(true);
        }
    }

    private static async Task<GalleryFrame> CaptureTipAsync(
        Window window,
        ShellViewModel shell,
        IServiceProvider services,
        GalleryOptions options,
        string tip,
        string themeKey)
    {
        var section = GalleryTips.Section(tip);

        if (!shell.TryNavigate(section))
        {
            throw new InvalidOperationException($"Страница «{section}» не открылась – подсказку «{tip}» негде показать.");
        }

        GalleryTips.Prepare(tip, services);
        await SettleAsync(window).ConfigureAwait(true);

        window.Left = 0;
        window.Top = 0;
        await SettleAsync(window).ConfigureAwait(true);

        var host = GalleryTips.Host(window);

        var tooltip = host.ShowTooltipForAutomation(GalleryTips.Point(tip, host))
            ?? throw new InvalidOperationException($"Подсказка «{tip}» не открылась – под точкой нет плитки.");

        try
        {
            await SettleAsync(window).ConfigureAwait(true);

            var file = $"{TipFilePrefix}{ViewCapture.Slug(tip)}-{themeKey}{ViewCapture.FileExtension}";

            var (width, height) = ViewCapture.Save(window,
                tooltip,
                ViewCapture.OffsetBetween(window, tooltip),
                Path.Combine(options.Directory, file),
                options.Scale);

            if (width == 0 || height == 0)
            {
                throw new InvalidOperationException("Окно не отрисовано – нулевой размер кадра.");
            }

            return new(TipKind, tip, themeKey, file, width, height);
        }
        finally
        {
            host.HideTooltipForAutomation();
            window.Left = OffScreen;
            window.Top = OffScreen;
            await SettleAsync(window).ConfigureAwait(true);
        }
    }

    private static FrameworkElement Target(Window window, GalleryOptions options)
    {
        if (options.Element.Length == 0)
        {
            return window;
        }

        return ViewCapture.Find(window, options.Element)
            ?? throw new InvalidOperationException($"Элемент «{options.Element}» не найден на этой странице.");
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

    private static async Task ArrangeAsync(IServiceProvider services, GalleryFixture fixture)
    {
        var scan = services.GetRequiredService<ScanViewModel>();
        await scan.ScanFromAutomationAsync(fixture.Left, CancellationToken.None).ConfigureAwait(true);
        scan.SelectedNode = scan.Roots.FirstOrDefault();

        var sync = services.GetRequiredService<SyncViewModel>();
        sync.Setup.LeftPath = fixture.Left;
        sync.Setup.RightPath = fixture.Right;
        await sync.CompareFromAutomationAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private static async Task SettleAsync(Window window)
    {
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        await Task.Delay(AppDefaults.GalleryFrameDelayMs).ConfigureAwait(true);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    private static void WriteIndex(GalleryOptions options, IReadOnlyList<GalleryFrame> frames)
    {
        var index = new GalleryIndex(AppInfo.Name, AppInfo.Version, options.Width, options.Height, options.Scale, options.Unknown, frames);
        File.WriteAllText(Path.Combine(options.Directory, IndexFileName), JsonSerializer.Serialize(index, JsonOptions));
    }
}
