using KeepShell.Diagnostics;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class DiagnosticsCaptureSession(DiagnosticsCollector collector, ILogger<DiagnosticsCaptureSession> logger)
{
    private readonly Lock _gate = new();

    private DiagnosticsBundle? _pending;

    private DiagnosticsCapturePreview? _preview;

    internal DiagnosticsCapturePreview Prepare(bool rawPaths)
    {
        logger.McpToolInvoked("prepare_diagnostics", rawPaths ? "пути как есть" : "пути обезличены");
        var bundle = collector.Build(rawPaths);
        var preview = new DiagnosticsCapturePreview(Guid.NewGuid().ToString("N"), DateTime.UtcNow, rawPaths, bundle.Describe());

        lock (_gate)
        {
            _pending = bundle;
            _preview = preview;
        }

        return preview;
    }

    internal DiagnosticsCaptureResult Save(string captureId)
    {
        logger.McpToolInvoked("save_diagnostics", captureId);
        lock (_gate)
        {
            if (_pending is null || _preview is null || !string.Equals(captureId, _preview.CaptureId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Подготовленный пакет не найден: он уже сохранён или заменён. Вызовите prepare_diagnostics и проверьте состав заново.");
            }

            var path = collector.Save(_pending);
            var result = new DiagnosticsCaptureResult(_preview.CaptureId, _preview.CapturedAtUtc, DateTime.UtcNow, path);
            _pending = null;
            _preview = null;

            return result;
        }
    }
}

internal sealed record DiagnosticsCapturePreview(string CaptureId, DateTime CapturedAtUtc, bool RawPaths, string Contents);

internal sealed record DiagnosticsCaptureResult(string CaptureId, DateTime CapturedAtUtc, DateTime SavedAtUtc, string Path);
