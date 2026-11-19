using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.Views.Dialogs;
using System.IO.Compression;
using System.Windows.Controls;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [Test]
    public void Полоса_хода_архивации_показывает_долю_упакованных_файлов()
    {
        ArchiveRequest request = new(
            _fixture.Left, Path.Combine(_fixture.Root, "progress.zip"), 4, 800, false, CompressionLevel.Optimal, false);
        var dialog = new ArchiveProgressDialogViewModel(request, new(), new FakeUiDispatcher(), NullLogger.Instance);

        try
        {
            _ = _shell.Modal.ShowAsync(dialog);
            dialog.ShowRunningForAutomation(1, 4, 800, Path.Combine(_fixture.Left, "first.txt"));
            Settle();

            var view = Descendant<ArchiveProgressDialogView>(_window);
            Assert.That(view, Is.Not.Null);
            var progress = Descendants<ProgressBar>(view!).Single(static bar => bar.IsVisible);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(progress.IsIndeterminate, Is.False);
                Assert.That(progress.Value, Is.EqualTo(0.25));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            dialog.ClearForAutomation();
            _shell.Modal.RequestCancel();
            Settle();
        }
    }
}
