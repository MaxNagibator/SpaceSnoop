using Microsoft.Extensions.DependencyInjection;

namespace SpaceSnoop.Wpf.ViewModels;

public abstract class DeferredPage : ObservableObject
{
    public abstract ObservableObject Content { get; }
}

public sealed class DeferredPage<TPage>(IServiceProvider services) : DeferredPage
    where TPage : ObservableObject
{
    public TPage Value => services.GetRequiredService<TPage>();

    public override ObservableObject Content => Value;
}
