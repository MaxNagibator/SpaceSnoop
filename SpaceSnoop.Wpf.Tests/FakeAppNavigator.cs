using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeAppNavigator : IAppNavigator
{
    public string? CurrentSectionKey { get; set; }

    public string? LastSection { get; private set; }

    public string? LastQuestion { get; private set; }

    public SyncProfile? LastProfile { get; private set; }

    public bool TryNavigate(string sectionKey)
    {
        LastSection = sectionKey;

        return true;
    }

    public void OpenSync(SyncProfile profile, ComparisonResult? comparison)
    {
        LastProfile = profile;
    }

    public void AskAgent(string question)
    {
        LastQuestion = question;
    }
}
