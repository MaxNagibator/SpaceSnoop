using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Duplicates;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class DuplicateMemberViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<SpaceBase>, bool, int> _mark;

    [ObservableProperty]
    private bool _isMarked;

    internal DuplicateMemberViewModel(DuplicateMember member, string? root, Func<IReadOnlyList<SpaceBase>, bool, int> mark)
    {
        _mark = mark;
        Space = member.Space;
        Path = member.Path;
        Kind = member.Kind;
        Name = DuplicateText.Name(member.Path);
        Directory = DuplicateText.Directory(member.Path, root);
        IsMarked = member.Space.IsDeleted;
    }

    public FileSpace Space { get; }

    public string Path { get; }

    public string Name { get; }

    public string Directory { get; }

    public DuplicateMemberKind Kind { get; }

    public bool ReclaimsSpace => Kind == DuplicateMemberKind.Copy;

    public string KindText => Kind switch
    {
        DuplicateMemberKind.HardLink => "жёсткая ссылка · тот же файл, места не вернёт",
        DuplicateMemberKind.SymbolicLink => "символьная ссылка · тот же файл, места не вернёт",
        _ => "копия",
    };

    public string StateText => ReclaimsSpace ? string.Empty : "ссылка · места не вернёт";

    public PackIconLucideKind KindIconKind => Kind switch
    {
        DuplicateMemberKind.HardLink => PackIconLucideKind.Link,
        DuplicateMemberKind.SymbolicLink => PackIconLucideKind.Link2,
        _ => PackIconLucideKind.Copy,
    };

    [RelayCommand]
    private void ToggleMark()
    {
        var target = !IsMarked;

        if (_mark([Space], target) > 0)
        {
            IsMarked = target;
        }
    }
}
