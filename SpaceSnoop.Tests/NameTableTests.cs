using SpaceSnoop.Core.Domain;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class NameTableTests
{
    [Test]
    public void Intern_DeduplicatesCaseSensitivelyAcrossAsciiCyrillicAndSurrogates()
    {
        var table = new NameTable(1);

        var idAscii1 = table.Intern("readme.txt");
        var idAscii2 = table.Intern("readme.txt");
        var idUpper = table.Intern("README.TXT");
        var idCyrillic1 = table.Intern("Отчёт_2026.xlsx");
        var idCyrillic2 = table.Intern("отчёт_2026.xlsx");
        var idSurrogate1 = table.Intern("файл_🚀_тест.bin");
        var idSurrogate2 = table.Intern(Encoding.UTF8.GetBytes("файл_🚀_тест.bin"));
        var idEmpty = table.Intern(ReadOnlySpan<char>.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(idAscii2, Is.EqualTo(idAscii1));
            Assert.That(idUpper, Is.Not.EqualTo(idAscii1));
            Assert.That(idCyrillic2, Is.Not.EqualTo(idCyrillic1));
            Assert.That(idSurrogate2, Is.EqualTo(idSurrogate1));
            Assert.That(table.GetString(idAscii1), Is.EqualTo("readme.txt"));
            Assert.That(table.GetString(idUpper), Is.EqualTo("README.TXT"));
            Assert.That(table.GetString(idCyrillic1), Is.EqualTo("Отчёт_2026.xlsx"));
            Assert.That(table.GetString(idCyrillic2), Is.EqualTo("отчёт_2026.xlsx"));
            Assert.That(table.GetString(idSurrogate1), Is.EqualTo("файл_🚀_тест.bin"));
            Assert.That(table.GetString(idEmpty), Is.Empty);
            Assert.That(table.GetUtf8(idEmpty).Length, Is.Zero);
        }
    }

    [Test]
    public void Equals_MatchesOrdinalAndIgnoreCaseWithoutAllocating()
    {
        var table = new NameTable(1);
        var asciiId = table.Intern("Config.JSON");
        var cyrillicId = table.Intern("Документ_🚀.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Equals(asciiId, "Config.JSON", StringComparison.Ordinal), Is.True);
            Assert.That(table.Equals(asciiId, "config.json", StringComparison.Ordinal), Is.False);
            Assert.That(table.Equals(asciiId, "config.json", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(table.Equals(asciiId, "Config.JSON", StringComparison.CurrentCulture), Is.True);
            Assert.That(table.Equals(asciiId, "other.json", StringComparison.OrdinalIgnoreCase), Is.False);

            Assert.That(table.Equals(cyrillicId, "Документ_🚀.txt", StringComparison.Ordinal), Is.True);
            Assert.That(table.Equals(cyrillicId, "документ_🚀.txt", StringComparison.Ordinal), Is.False);
            Assert.That(table.Equals(cyrillicId, "документ_🚀.txt", StringComparison.OrdinalIgnoreCase), Is.True);
        }
    }

    [Test]
    public void Intern_SupportsLongNamesAboveStackThreshold()
    {
        var table = new NameTable(1);
        var longName = new string('ж', 600) + "_🚀";
        var id = table.Intern(longName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.GetString(id), Is.EqualTo(longName));
            Assert.That(table.Equals(id, longName, StringComparison.Ordinal), Is.True);
        }
    }

    [Test]
    public void Intern_64ShardsIsThreadSafeAndPostFreezeAppendWorks()
    {
        var table = new NameTable(64);
        const int threadCount = 8;
        const int namesPerThread = 2000;
        var ids = new int[threadCount][];

        Parallel.For(
            0,
            threadCount,
            x =>
            {
                var local = new int[namesPerThread];

                for (var i = 0; i < namesPerThread; i++)
                {
                    local[i] = table.Intern($"shared_{i:D4}_{x & 1}.dat");
                }

                ids[x] = local;
            });

        for (var i = 0; i < namesPerThread; i++)
        {
            Assert.That(ids[2][i], Is.EqualTo(ids[0][i]));
            Assert.That(ids[3][i], Is.EqualTo(ids[1][i]));
            Assert.That(table.GetString(ids[0][i]), Is.EqualTo($"shared_{i:D4}_0.dat"));
        }

        table.Freeze();

        var postId = table.Intern("shared_0000_0.dat");
        var newPostId = table.Intern("post_freeze_файл.zip");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(postId, Is.Not.EqualTo(ids[0][0]));
            Assert.That(table.GetString(postId), Is.EqualTo("shared_0000_0.dat"));
            Assert.That(table.GetString(newPostId), Is.EqualTo("post_freeze_файл.zip"));
        }
    }

    [Test]
    public void Intern_GeometricGrowthFreezeTrimAndPostFreezeAppendWork()
    {
        var table = new NameTable(1);
        const int count = 17_000;
        var ids = new int[count];

        for (var i = 0; i < count; i++)
        {
            ids[i] = table.Intern($"geometric_entry_{i:D5}.bin");
        }

        table.Freeze();

        var postIds = new int[200];

        for (var i = 0; i < postIds.Length; i++)
        {
            postIds[i] = table.Intern($"post_trim_{i:D3}.txt");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.GetString(ids[0]), Is.EqualTo("geometric_entry_00000.bin"));
            Assert.That(table.GetString(ids[15]), Is.EqualTo("geometric_entry_00015.bin"));
            Assert.That(table.GetString(ids[16]), Is.EqualTo("geometric_entry_00016.bin"));
            Assert.That(table.GetString(ids[count - 1]), Is.EqualTo($"geometric_entry_{count - 1:D5}.bin"));
            Assert.That(table.GetString(postIds[0]), Is.EqualTo("post_trim_000.txt"));
            Assert.That(table.GetString(postIds[^1]), Is.EqualTo("post_trim_199.txt"));
        }
    }

    [Test]
    public void Intern_PreservesUnpairedUtf16SurrogatesDistinctFromReplacementChar()
    {
        var table = new NameTable(1);

        const string loneHigh = "a\uD800b";
        const string loneLow = "a\uDC00b";
        const string replacement = "a\uFFFDb";
        const string validPair = "x_\uD83D\uDE00_y";
        const string unpairedHigh = "x_\uD83D_y";
        const string unpairedLow = "x_\uDE00_y";

        var idHigh = table.Intern(loneHigh);
        var idHighRepeat = table.Intern(loneHigh);
        var idLow = table.Intern(loneLow);
        var idReplacement = table.Intern(replacement);
        var idValidPair = table.Intern(validPair);
        var idUnpairedHigh = table.Intern(unpairedHigh);
        var idUnpairedLow = table.Intern(unpairedLow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(idHighRepeat, Is.EqualTo(idHigh));
            Assert.That(idHigh, Is.Not.EqualTo(idLow));
            Assert.That(idHigh, Is.Not.EqualTo(idReplacement));
            Assert.That(idLow, Is.Not.EqualTo(idReplacement));
            Assert.That(idUnpairedHigh, Is.Not.EqualTo(idUnpairedLow));
            Assert.That(idUnpairedHigh, Is.Not.EqualTo(idValidPair));

            Assert.That(table.GetString(idHigh), Is.EqualTo(loneHigh));
            Assert.That(table.GetString(idLow), Is.EqualTo(loneLow));
            Assert.That(table.GetString(idReplacement), Is.EqualTo(replacement));
            Assert.That(table.GetString(idValidPair), Is.EqualTo(validPair));
            Assert.That(table.GetString(idUnpairedHigh), Is.EqualTo(unpairedHigh));
            Assert.That(table.GetString(idUnpairedLow), Is.EqualTo(unpairedLow));

            Assert.That(table.Equals(idHigh, loneHigh, StringComparison.Ordinal), Is.True);
            Assert.That(table.Equals(idHigh, replacement, StringComparison.Ordinal), Is.False);
            Assert.That(table.Equals(idHigh, replacement, StringComparison.OrdinalIgnoreCase), Is.False);
            Assert.That(table.Equals(idReplacement, loneHigh, StringComparison.Ordinal), Is.False);
            Assert.That(table.Equals(idValidPair, validPair, StringComparison.OrdinalIgnoreCase), Is.True);
        }
    }

    [Test]
    public void NeedsOverflow_RoutesAtMaxRegularChunkBoundary()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(NameTable.MaxRegularChunks, Is.EqualTo(2048));
            Assert.That(NameTable.NeedsOverflow(100, 1024, 2047, 500, 1024), Is.False);
            Assert.That(NameTable.NeedsOverflow(100, 1024, 2047, 1000, 1024), Is.False);
            Assert.That(NameTable.NeedsOverflow(100, 1024, 2048, 500, 1024), Is.False);
            Assert.That(NameTable.NeedsOverflow(100, 1024, 2048, 1000, 1024), Is.True);
            Assert.That(NameTable.NeedsOverflow(1025, 1024, 1, 0, 1024), Is.True);
        }
    }

    [Test]
    public void ComputeResizeThreshold_AvoidsInt32Overflow()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(NameTable.ComputeResizeThreshold(33_554_432), Is.EqualTo(21_810_380));
            Assert.That(NameTable.ComputeResizeThreshold(1 << 30), Is.GreaterThan(0));
        }
    }

    [Test]
    public void GetUtf8_LockFreeConcurrentReadsMatch64ShardInternAndPrecomputedHash()
    {
        var table = new NameTable(64);
        var published = new System.Collections.Concurrent.ConcurrentQueue<(int Id, byte[] Bytes)>();
        const int writerCount = 6;
        const int perWriter = 3000;
        var writersDone = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var writers = new Task[writerCount];

        for (var i = 0; i < writerCount; i++)
        {
            var writerIndex = i;
            writers[i] = Task.Run(() =>
            {
                try
                {
                    for (var j = 0; j < perWriter; j++)
                    {
                        var bytes = Encoding.UTF8.GetBytes($"concurrent_{writerIndex}_{j:D4}_имя.dat");
                        var hash = NameTable.HashUtf8(bytes);
                        var id = table.Intern(bytes, hash);
                        published.Enqueue((id, bytes));
                    }
                }
                finally
                {
                    Interlocked.Increment(ref writersDone);
                }
            });
        }

        var readers = new Task[4];

        for (var i = 0; i < readers.Length; i++)
        {
            readers[i] = Task.Run(() =>
            {
                while ((Volatile.Read(ref writersDone) < writerCount || !published.IsEmpty) && !timeout.IsCancellationRequested)
                {
                    if (!published.TryDequeue(out var item))
                    {
                        Thread.Yield();
                        continue;
                    }

                    Assert.That(table.GetUtf8(item.Id).SequenceEqual(item.Bytes), Is.True);
                }
            });
        }

        var finished = Task.WaitAll([.. writers, .. readers], TimeSpan.FromSeconds(30));
        Assert.That(finished, Is.True);
        table.Freeze();

        var probe = Encoding.UTF8.GetBytes("concurrent_0_0123_имя.dat");
        var probeHash = NameTable.HashUtf8(probe);
        var probeId = table.Intern(probe, probeHash);
        Assert.That(table.GetUtf8(probeId).SequenceEqual(probe), Is.True);
    }
}
