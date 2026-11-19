using System.IO.Enumeration;

namespace SpaceSnoop.Core;

public sealed class ExclusionFilter(string commaSeparatedPatterns)
{
    private readonly string[] _patterns = string.IsNullOrWhiteSpace(commaSeparatedPatterns)
        ? []
        : commaSeparatedPatterns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsExcluded(string name)
    {
        return _patterns.Any(x => FileSystemName.MatchesSimpleExpression(x, name));
    }
}
