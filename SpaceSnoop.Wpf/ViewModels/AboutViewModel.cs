using KeepShell.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class AboutViewModel(ErrorReportService errorReports, ILogger<AboutViewModel> logger)
    : ObservableObject, IPageHeader
{
    public string AppName => AppInfo.Name;

    public string Version => AppInfo.Version;

    public string RepoSlug => AppInfo.RepoSlug;

    public string Tagline => "Шнырь по твоим накопителям — покажет, где залёг весь хлам";

    public string Description =>
        "Исследуй бескрайние просторы своих накопителей и почувствуй себя археологом: SpaceSnoop "
        + "откопает древние артефакты среди данных и покажет занятое место деревом, где жирные папки "
        + "краснеют от стыда под тепловой подсветкой. А ещё сведёт две папки лицом к лицу и "
        + "синхронизирует их. Дай накопителям дышать полной грудью — убери весь хлам!";

    public string DotNetVersion => RuntimeInformation.FrameworkDescription;

    public string OperatingSystem => RuntimeInformation.OSDescription;

    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

    public bool IsElevated { get; } = AdminElevation.IsElevated;

    public string ElevationCaption => IsElevated
        ? "Админ на борту — шныряем где угодно"
        : "Обычный режим — за пару дверей не пустят, запусти от админа для полного шныряния";

    public string PageTitle => "О программе";

    public string PageDescription => "Что за шнырь, что у него под капотом и куда бежать, если что-то сломалось.";

    [RelayCommand]
    private void OpenRepository()
    {
        OpenUrl(AppInfo.RepositoryUrl);
    }

    [RelayCommand]
    private void OpenReleases()
    {
        OpenUrl(AppInfo.ReleasesUrl);
    }

    [RelayCommand]
    private void ReportIssue()
    {
        try
        {
            var payload = errorReports.Build(null, "Отчёт из раздела «О программе»");

            try
            {
                Clipboard.SetText(payload.LogClipboard);
            }
            catch (Exception clipEx)
            {
                logger.ClipboardLogSectionFailed(clipEx);
            }

            Process.Start(new ProcessStartInfo(payload.IssueUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.ReportFormFailed(ex);
        }
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var text = new StringBuilder()
            .AppendLine($"{AppName} {Version}")
            .AppendLine($".NET: {DotNetVersion}")
            .AppendLine($"ОС: {OperatingSystem}")
            .AppendLine($"Архитектура: {Architecture}")
            .Append($"Права: {(IsElevated ? "Администратор" : "Обычный режим")}")
            .ToString();

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            logger.DiagnosticsCopyFailed(ex);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.OpenUrlFailed(ex, url);
        }
    }
}
