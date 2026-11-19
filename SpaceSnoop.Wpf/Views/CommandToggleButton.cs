using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Views;

public sealed class CommandToggleButton : ToggleButton
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new ToggleByClickPeer(this);
    }

    private sealed class ToggleByClickPeer(CommandToggleButton owner) : ToggleButtonAutomationPeer(owner), IToggleProvider
    {
        ToggleState IToggleProvider.ToggleState => owner.IsChecked switch
        {
            true => ToggleState.On,
            false => ToggleState.Off,
            null => ToggleState.Indeterminate,
        };

        void IToggleProvider.Toggle()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            owner.OnClick();
        }
    }
}
