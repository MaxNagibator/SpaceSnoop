using System.Collections;
using SpaceSnoop.Sorters;

namespace SpaceSnoop;

public class SorterMode(string name, IComparer comparer)
{
    public static readonly SorterMode ByName = new("По имени", new NodeSorterByName());
    public static readonly SorterMode BySize = new("По размеру", new NodeSorterByTotalSize());
    public static readonly SorterMode ByDate = new("По дате создания", new NodeSorterByDate());
    public static readonly SorterMode ByLastAccessTime = new("По времени последнего доступа", new NodeSorterByLastAccessTime());

    public string Name { get; } = name;
    public IComparer Comparer { get; } = comparer;

    public override string ToString()
    {
        return Name;
    }
}
