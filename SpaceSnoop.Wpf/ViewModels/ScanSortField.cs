namespace SpaceSnoop.Wpf.ViewModels;

/// <summary>Поле, по которому сортируется дерево сканирования.</summary>
public enum ScanSortField
{
    /// <summary>По имени.</summary>
    Name = 0,

    /// <summary>По размеру.</summary>
    Size = 1,

    /// <summary>По дате создания.</summary>
    CreationDate = 2,

    /// <summary>По времени последнего доступа.</summary>
    LastAccessTime = 3,
}
