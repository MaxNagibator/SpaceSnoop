using System.Windows.Data;

namespace SpaceSnoop.Wpf.Views;

public sealed class DeferredPageView : ContentControl, IView<DeferredPage>
{
    public DeferredPageView()
    {
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        SetBinding(ContentProperty, new Binding(nameof(DeferredPage.Content)) { Mode = BindingMode.OneWay });
    }
}
