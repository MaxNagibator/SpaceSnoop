namespace SpaceSnoop.Core;

public readonly record struct PathCaseRules(StringComparer Left, StringComparer Right)
{
    public static PathCaseRules Insensitive { get; } = new(StringComparer.OrdinalIgnoreCase, StringComparer.OrdinalIgnoreCase);

    public static PathCaseRules Sensitive { get; } = new(StringComparer.Ordinal, StringComparer.Ordinal);

    public StringComparer Match => PathCase.Stricter(Left, Right);

    public bool MatchIsSensitive => PathCase.IsSensitive(Match);

    // TODO: чувствительность снимается один раз с корня каждой стороны и наследуется всем поддеревом –
    // per-directory режим NTFS (fsutil setCaseSensitiveInfo) и вложенный том внутри дерева дадут неверную
    // политику ниже корня; переснимать на каждом каталоге, когда появится смешанный том в работе.
    public static PathCaseRules For(string leftPath, string rightPath)
    {
        return new(PathCase.ComparerFor(leftPath), PathCase.ComparerFor(rightPath));
    }
}
