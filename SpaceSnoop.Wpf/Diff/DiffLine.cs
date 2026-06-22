using System.Globalization;

namespace SpaceSnoop.Wpf.Diff;

public sealed record DiffLine(DiffLineKind Kind, string Text, int LeftNumber, int RightNumber)
{
    public string LeftNumberText => LeftNumber > 0 ? LeftNumber.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public string RightNumberText => RightNumber > 0 ? RightNumber.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public string Sign => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "−",
        _ => string.Empty,
    };
}
