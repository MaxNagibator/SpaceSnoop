using KeepShell.Services;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Wpf.ViewModels.About;

public sealed partial class AboutViewModel(
    ErrorReportService errorReports,
    AppUpdateViewModel updater,
    IAppNavigator navigator,
    ToastNotifier notifier,
    IClipboardService clipboard,
    IShellLauncher shell,
    ILogger<AboutViewModel> logger)
    : ObservableObject, IPageHeader
{
    private static readonly BuildBadge Badge = BuildBadge.Build(
        BuildStamp.Read(Environment.ProcessPath, AppInfo.InformationalVersion),
        AppInfo.InformationalVersion,
        AppStorage.PortableDirectory,
        "отметки сборки рядом нет, поэтому коммит и дата неизвестны");

    public string AppName => AppInfo.Name;

    public string Version => AppInfo.Version;

    public string BuildInfo => Badge.Value;

    public string BuildInfoTooltip => Badge.Tooltip;

    public string RepoSlug => AppInfo.RepoSlug;

    public string Tagline => "Шнырь по твоим накопителям – покажет, где залёг весь хлам";

    public string DotNetVersion => RuntimeInformation.FrameworkDescription;

    public string OperatingSystem => RuntimeInformation.OSDescription;

    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

    public bool IsElevated { get; } = AdminElevation.IsElevated;

    public string ElevationCaption => IsElevated
        ? "Админ на борту – шныряем где угодно"
        : "Обычный режим – за пару дверей не пустят, запусти от админа для полного шныряния";

    public string PageTitle => "О программе";

    public string PageDescription => "Что за шнырь, что у него под капотом и куда бежать, если что-то сломалось.";

    public AppUpdateViewModel Updater => updater;

    [RelayCommand]
    private void OpenSection(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return;
        }

        if (!navigator.TryNavigate(sectionKey) && string.Equals(sectionKey, SectionKey.Chat, StringComparison.OrdinalIgnoreCase))
        {
            navigator.TryNavigate(SectionKey.Settings);
        }
    }

    [RelayCommand]
    private void OpenRepository()
    {
        OpenUrl(updater.RepositoryUrl);
    }

    [RelayCommand]
    private void OpenReleases()
    {
        OpenUrl(updater.ReleasesUrl);
    }

    [RelayCommand]
    private void OpenReleaseUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            OpenUrl(url);
        }
    }

    [RelayCommand]
    private void ReportIssue()
    {
        try
        {
            var payload = errorReports.Build(null, "Отчёт из раздела «О программе»");
            var copied = clipboard.TrySetText(payload.LogClipboard);

            if (!shell.Open(payload.IssueUrl))
            {
                notifier.Notify("Не удалось открыть форму отчёта в браузере.", StatusSeverity.Warning);
                return;
            }

            notifier.Notify(
                copied
                    ? "Хвост журнала скопирован в буфер обмена – вставьте его в открывшуюся форму."
                    : "Форма отчёта открыта, но скопировать журнал в буфер обмена не удалось.",
                copied ? StatusSeverity.Info : StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            logger.ReportFormFailed(ex);
            notifier.Notify("Не удалось подготовить отчёт об ошибке.", StatusSeverity.Error);
        }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var text = new StringBuilder()
            .AppendLine($"{AppName} {Version}")
            .AppendLine($"Сборка: {BuildInfoTooltip}")
            .AppendLine($".NET: {DotNetVersion}")
            .AppendLine($"ОС: {OperatingSystem}")
            .AppendLine($"Архитектура: {Architecture}")
            .Append($"Права: {(IsElevated ? "Администратор" : "Обычный режим")}")
            .ToString();

        if (clipboard.TrySetText(text))
        {
            notifier.Notify("Сведения об окружении скопированы в буфер обмена.", StatusSeverity.Success);
            return;
        }

        notifier.Notify("Не удалось скопировать сведения об окружении в буфер обмена.", StatusSeverity.Warning);
    }

    private void OpenUrl(string url)
    {
        shell.Open(url);
    }
}
