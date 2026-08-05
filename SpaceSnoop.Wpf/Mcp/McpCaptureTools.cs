using ModelContextProtocol;
using System.IO;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpCaptureTools(McpNavigator navigator, ILogger logger)
{
    public string CaptureView(string? section, string? element, double scale)
    {
        section = section?.Trim();
        element = element?.Trim();
        scale = Math.Clamp(scale, AppDefaults.ViewCaptureScaleMin, AppDefaults.ViewCaptureScaleMax);

        logger.McpToolInvoked("capture_view", $"страница {(section is { Length: > 0 } ? section : "текущая")}, элемент {(element is { Length: > 0 } ? element : "всё окно")}, масштаб {scale:0.##}");

        var deferred = false;

        if (section is { Length: > 0 })
        {
            if (!SectionKey.IsNavigable(section))
            {
                throw new McpException($"Неизвестная страница «{section}». Доступны: {string.Join(", ", SectionKey.Navigable)}.");
            }

            deferred = McpDispatch.Run(() => navigator.DeferOrNavigate(section));
        }

        McpDispatch.Run(static () => true, DispatcherPriority.ContextIdle);

        return McpDispatch.Run(() => CaptureCore(element, scale, deferred));
    }

    private string CaptureCore(string? element, double scale, bool deferred)
    {
        if (Application.Current?.MainWindow is not { } window)
        {
            throw new McpException("Окно приложения не открыто – снимать нечего.");
        }

        window.UpdateLayout();

        var named = element is { Length: > 0 };

        var target = named
            ? ViewCapture.Find(window, element!) ?? throw new McpException($"В окне нет элемента с именем «{element}». Открыты, например: {string.Join(", ", ViewCapture.Names(window, AppDefaults.ViewCaptureNamesHint))}.")
            : window;

        var page = navigator.CurrentSectionKey ?? "-";
        var label = named ? $"{page}-{element}" : page;
        var path = Path.Combine(ViewCapture.DirectoryPath, ViewCapture.FileName(label, DateTimeOffset.Now));

        ViewCapture.DropObsolete(ViewCapture.DirectoryPath, AppDefaults.ViewCaptureLimit - 1, logger.ViewCaptureFailed);

        int width;
        int height;

        try
        {
            (width, height) = ViewCapture.Save(target, path, scale);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.ViewCaptureFailed(exception, path);
            throw new McpException($"Снимок не записан в «{path}»: {exception.Message}");
        }

        if (width == 0 || height == 0)
        {
            throw new McpException(named
                ? $"Элемент «{element}» не отрисован – он скрыт или ещё не построен на текущей странице."
                : "Окно не отрисовано – оно свёрнуто или ещё не показано.");
        }

        logger.ViewCaptured(path, width, height);

        return McpFormat.Serialize(new McpCapture(path,
            page,
            named ? element : null,
            ThemeManager.Current?.Key ?? "-",
            width,
            height,
            McpFormat.DescribeDeferredNavigation(deferred)));
    }
}
