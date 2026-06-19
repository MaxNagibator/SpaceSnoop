using KeepShell.Services.Modal;
using SpaceSnoop.Wpf.Diff;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class FileDiffDialogViewModel(
    string name,
    string leftPath,
    string rightPath,
    IReadOnlyList<DiffRow> rows,
    int added,
    int removed)
    : ObservableObject, IDialogViewModel
{
    public event EventHandler<bool>? RequestClose;

    public string Title => "Сравнение содержимого";

    public string Name { get; } = name;

    public string LeftPath { get; } = leftPath;

    public string RightPath { get; } = rightPath;

    public IReadOnlyList<DiffRow> Rows { get; } = rows;

    public int Added { get; } = added;

    public int Removed { get; } = removed;

    public bool HasChanges => Added > 0 || Removed > 0;

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, false);
    }
}
