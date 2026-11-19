using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class BlockingRootsCheck : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    private readonly Func<string, string, string?> _original = SyncRootsCheck.Refusal;
    private readonly ManualResetEventSlim _entered = new();
    private readonly ManualResetEventSlim _release = new();

    public BlockingRootsCheck()
    {
        SyncRootsCheck.Refusal = Refuse;
    }

    public void WaitEntered()
    {
        Assert.That(_entered.Wait(Ceiling), Is.True, "проверка корней так и не началась");
    }

    public void Release()
    {
        _release.Set();
    }

    public void Dispose()
    {
        _release.Set();
        SyncRootsCheck.Refusal = _original;
    }

    private string? Refuse(string left, string right)
    {
        _entered.Set();
        _release.Wait(Ceiling);
        return _original(left, right);
    }
}
