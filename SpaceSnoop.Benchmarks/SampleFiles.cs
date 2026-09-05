namespace SpaceSnoop.Benchmarks;

internal sealed class SampleFiles : IDisposable
{
    private const int SizeExponentBits = 16;

    private readonly string _root;

    private SampleFiles(string root, FileInfo[] files)
    {
        _root = root;
        Files = files;
    }

    public FileInfo[] Files { get; }

    public static SampleFiles Create(int count)
    {
        var root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopBench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var samples = new SampleFiles(root, new FileInfo[count]);

        try
        {
            var random = new Random(SyntheticTree.Seed);

            for (var i = 0; i < count; i++)
            {
                var path = Path.Combine(root, $"sample{i}.bin");
                var size = (long)Math.Pow(2, random.NextDouble() * SizeExponentBits);

                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    stream.SetLength(size);
                }

                samples.Files[i] = new(path);
            }
        }
        catch
        {
            samples.Dispose();
            throw;
        }

        return samples;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
