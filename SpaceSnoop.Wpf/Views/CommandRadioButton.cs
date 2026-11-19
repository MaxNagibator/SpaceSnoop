using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace SpaceSnoop.Wpf.Views;

public sealed class CommandRadioButton : RadioButton
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new SelectByClickPeer(this);
    }

    private sealed class SelectByClickPeer(CommandRadioButton owner) : RadioButtonAutomationPeer(owner), ISelectionItemProvider
    {
        bool ISelectionItemProvider.IsSelected => owner.IsChecked == true;

        IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer => null;

        void ISelectionItemProvider.Select()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            owner.OnClick();
        }

        void ISelectionItemProvider.AddToSelection()
        {
            if (owner.IsChecked != true)
            {
                throw new InvalidOperationException();
            }
        }

        void ISelectionItemProvider.RemoveFromSelection()
        {
            if (owner.IsChecked == true)
            {
                throw new InvalidOperationException();
            }
        }
    }
}
