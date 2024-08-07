namespace SpaceSnoop.Core;

public class SpaceColorCalculator
{
    private const int MaxComponentValue = 255;

    public const int MinIntensity = 1;
    public const int MaxIntensity = 50;
    public const int DefaultIntensity = 10;

    private int _intensity = DefaultIntensity;

    public int Intensity
    {
        get => _intensity;
        set
        {
            if (value is < MinIntensity or > MaxIntensity)
            {
                return;
            }

            _intensity = value;
        }
    }

    public Color GetColorBasedOnSize(DirectorySpace directory, long maxSize)
    {
        return GetColor(directory.TotalSize, maxSize);
    }

    public Color GetColorBasedOnSize(FileSpace file, long maxSize)
    {
        return GetColor(file.Size, maxSize);
    }

    private Color GetColor(long size, long maxSize)
    {
        double ratio = Math.Min((double)size / maxSize, 1);

        int red = (int)(MaxComponentValue * ratio * Intensity);
        red = Math.Max(0, Math.Min(MaxComponentValue, red));
        return Color.FromArgb(red, 0, 0);
    }
}
