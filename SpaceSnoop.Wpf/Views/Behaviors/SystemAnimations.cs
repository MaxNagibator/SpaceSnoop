using System.ComponentModel;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public sealed class SystemAnimations : ObservableObject
{
    public static SystemAnimations Current { get; } = new();

    public bool Enabled => SystemParameters.ClientAreaAnimation;

    private SystemAnimations()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SystemParameters.ClientAreaAnimation))
        {
            OnPropertyChanged(nameof(Enabled));
        }
    }
}
