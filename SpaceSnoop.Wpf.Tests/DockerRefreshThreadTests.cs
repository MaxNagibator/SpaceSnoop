using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Docker;
using SpaceSnoop.Wpf.ViewModels.Docker;
using System.Windows.Data;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class DockerRefreshThreadTests
{
    private const string Usage = """
        {"Type":"Images","TotalCount":"2","Active":"1","Size":"8.2GB","Reclaimable":"3.6GB (44%)"}
        {"Type":"Build Cache","TotalCount":"12","Active":"0","Size":"1.4GB","Reclaimable":"1.4GB (100%)"}
        """;

    private const string Inventory = """
        {"Images":[{"ID":"sha256:1a2b","Repository":"nginx","Tag":"latest","Size":"142MB","Containers":"1","CreatedSince":"2 days ago"}]}
        """;

    [Test]
    public void Опрос_Docker_вне_потока_диспетчера_наполняет_привязанные_списки()
    {
        var view = new DockerViewModel(
            new DockerService(new YieldingRunner()),
            new NoopDialogs(),
            new DispatcherUiDispatcher(Dispatcher.CurrentDispatcher),
            NullLogger<DockerViewModel>.Instance);

        var buckets = CollectionViewSource.GetDefaultView(view.Buckets);
        var groups = CollectionViewSource.GetDefaultView(view.Groups);

        Task.Run(() => view.RefreshCommand.ExecuteAsync(null)).GetAwaiter().GetResult();

        VisualTestHost.Pump();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(view.IsAvailable, Is.True, view.UnavailableReason);
            Assert.That(buckets.IsEmpty, Is.False, "Полоса категорий осталась пустой.");
            Assert.That(groups.IsEmpty, Is.False, "Список объектов остался пустым.");
        }
    }

    private sealed class YieldingRunner : IDockerProcessRunner
    {
        public async Task<DockerProcessResult> RunAsync(
            string fileName,
            string arguments,
            DockerProcessOptions options,
            CancellationToken cancel = default)
        {
            await Task.Yield();

            return arguments.Contains("-v", StringComparison.Ordinal)
                ? new(0, Inventory, string.Empty)
                : new(0, Usage, string.Empty);
        }
    }

    private sealed class DispatcherUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
    {
        public bool HasAccess => dispatcher.CheckAccess();

        public void Invoke(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();

                return;
            }

            dispatcher.BeginInvoke(action);
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        {
            return new FakeUiTimer(interval, tick);
        }
    }
}
