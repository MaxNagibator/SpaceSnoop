using CommunityToolkit.Mvvm.Input;
using SpaceSnoop.Wpf.Views;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class CommandButtonAutomationTests
{
    [Test]
    public void Пометка_дубликата_через_UI_Automation_выполняет_команду()
    {
        var runs = 0;
        var button = new CommandToggleButton
        {
            Command = new RelayCommand(() => runs++),
        };
        var peer = UIElementAutomationPeer.CreatePeerForElement(button);

        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();

        Assert.That(runs, Is.EqualTo(1));
    }
}
