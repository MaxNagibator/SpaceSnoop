namespace SpaceSnoop.Extensions;

public static class DirectorySpaceExtension
{
    public static Color GetColorBasedOnSize(this DirectorySpace directory, long maxSize)
    {
        double ratio = Math.Min((double)directory.TotalSize / maxSize, 1);

        double intensity = 10;
        int maxComponentValue = 255;

        int red = (int)(maxComponentValue * ratio * intensity);
        red = Math.Max(0, Math.Min(255, red));
        return Color.FromArgb(red, 0, 0);
    }
}
