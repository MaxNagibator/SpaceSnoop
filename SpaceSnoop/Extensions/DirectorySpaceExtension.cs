namespace SpaceSnoop.Extensions;

public static class DirectorySpaceExtension
{
    public static Color GetColorBasedOnSize(this DirectorySpace directory, long maxSize)
    {
        double ratio = Math.Min((double)directory.TotalSize / maxSize, 1);

        double intensity = 10;
        int maxComponentValue = 255;

        int red = (int)(maxComponentValue * ratio * intensity);

        return Color.FromArgb(red > maxComponentValue ? maxComponentValue : red, 0, 0);
    }
}
