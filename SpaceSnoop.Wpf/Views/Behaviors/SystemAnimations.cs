using System.ComponentModel;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public sealed class SystemAnimations : ObservableObject
{
    private SystemAnimations()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    public static SystemAnimations Current { get; } = new();

    public bool Enabled => SystemParameters.ClientAreaAnimation;

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(SystemParameters.ClientAreaAnimation))
        {
            OnPropertyChanged(nameof(Enabled));
        }
    }
}
