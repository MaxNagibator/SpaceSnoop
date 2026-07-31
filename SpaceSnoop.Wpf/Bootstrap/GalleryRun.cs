using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Views;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Bootstrap;

internal sealed record GalleryFrame(string Page, string Theme, string File, int Width, int Height);

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

        Directory.CreateDirectory(options.Directory);
        logger.GalleryStarted(options.Pages.Count, options.Themes.Count, options.Directory);

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
        var (width, height) = ViewCapture.Save(window, Path.Combine(options.Directory, file), options.Scale);

        if (width == 0 || height == 0)
        {
            throw new InvalidOperationException("Окно не отрисовано – нулевой размер кадра.");
        }

        return new(page, themeKey, file, width, height);
    }

    private static async Task ArrangeAsync(IServiceProvider services, GalleryFixture fixture)
    {
        var scan = services.GetRequiredService<ScanViewModel>();
        await scan.ScanFromAutomationAsync(fixture.Left, CancellationToken.None).ConfigureAwait(true);

        var sync = services.GetRequiredService<SyncViewModel>();
        sync.LeftPath = fixture.Left;
        sync.RightPath = fixture.Right;
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
