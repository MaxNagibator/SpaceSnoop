using System.Windows.Input;

namespace SpaceSnoop.Wpf.Views.Sync;

public partial class SyncDiffPanel : UserControl
{
    public SyncDiffPanel()
    {
        InitializeComponent();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SyncNodeViewModel node } && node.CompareContentCommand.CanExecute(null))
        {
            node.CompareContentCommand.Execute(null);
        }
    }
}
