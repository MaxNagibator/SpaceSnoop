using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed record GalleryFixture(string Root, string Left, string Right);

public static class GalleryFixtures
{
    public const string FolderName = "spacesnoop-gallery";

    private static readonly DateTime Base = new(2025, 3, 14, 9, 30, 0, DateTimeKind.Local);

    public static GalleryFixture Create()
    {
        var root = Path.Combine(Path.GetTempPath(), FolderName);
        Reset(root);

        var left = Path.Combine(root, "проект");
        var right = Path.Combine(root, "бэкап");

        WriteTree(left);
        WriteTree(right);

        File.Delete(Path.Combine(right, "docs", "план.txt"));
        Write(Path.Combine(right, "media", "обложка.png"), 512_000, Base.AddDays(-2));
        Write(Path.Combine(right, "docs", "README.md"), 12_000, Base.AddHours(-30));
        Write(Path.Combine(right, "архив", "прошлый-год.zip"), 3_400_000, Base.AddDays(-40));

        return new(root, left, right);
    }

    public static IReadOnlyList<SyncProfile> Profiles(GalleryFixture fixture)
    {
        return
        [
            new()
            {
                Id = "gallery-docs",
                Name = "Документы на диск D",
                Left = Path.Combine(fixture.Left, "docs"),
                Right = Path.Combine(fixture.Right, "docs"),
                Mode = SyncProfile.IndexOfMode(SyncMode.LeftToRight),
                Interval = ScheduleInterval.Daily,
                Time = "03:00",
            },
            new()
            {
                Id = "gallery-media",
                Name = "Медиа на внешний диск",
                Left = Path.Combine(fixture.Left, "media"),
                Right = Path.Combine(fixture.Right, "media"),
                Mode = SyncProfile.IndexOfMode(SyncMode.Bidirectional),
                Winner = SyncWinner.Newest,
                Interval = ScheduleInterval.OnLogon,
                Time = "22:15",
            },
            new()
            {
                Id = "gallery-project",
                Name = "Проект целиком",
                Left = fixture.Left,
                Right = fixture.Right,
                Mode = SyncProfile.IndexOfMode(SyncMode.LeftToRight),
                Exclusions = "bin,obj,*.tmp",
                Mirror = true,
                Interval = ScheduleInterval.Hourly,
                Time = "00:30",
                SkipInBatch = true,
            },
        ];
    }

    private static void Reset(string root)
    {
        if (Directory.Exists(root) && string.Equals(Path.GetFileName(root), FolderName, StringComparison.Ordinal))
        {
            Directory.Delete(root, true);
        }

        Directory.CreateDirectory(root);
    }

    private static void WriteTree(string root)
    {
        Write(Path.Combine(root, "docs", "README.md"), 11_800, Base.AddHours(-4));
        Write(Path.Combine(root, "docs", "спецификация.docx"), 348_000, Base.AddDays(-9));
        Write(Path.Combine(root, "docs", "план.txt"), 2_400, Base.AddDays(-1));

        Write(Path.Combine(root, "media", "разбор-логов.mp4"), 6_200_000, Base.AddDays(-3));
        Write(Path.Combine(root, "media", "обложка.png"), 806_000, Base.AddDays(-2));

        Write(Path.Combine(root, "build", "SpaceSnoop.exe"), 1_450_000, Base.AddHours(-2));
        Write(Path.Combine(root, "build", "SpaceSnoop.pdb"), 920_000, Base.AddHours(-2));

        for (var index = 0; index < 40; index++)
        {
            Write(Path.Combine(root, "cache", $"chunk-{index:00}.bin"), 4_096 + (index * 512), Base.AddMinutes(-index));
        }
    }

    private static void Write(string path, int size, DateTime modified)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTime(path, modified);
    }
}
