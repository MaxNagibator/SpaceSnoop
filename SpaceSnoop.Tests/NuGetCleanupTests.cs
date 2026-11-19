using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using System.ComponentModel;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class NuGetCleanupTests
{
    private string _root = null!;
    private Dictionary<NuGetCacheKind, string> _paths = null!;
    private string _context = null!;
    private NuGetCleanupService _service = null!;
    private CapturingLogger<NuGetCleanupService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "SpaceSnoop.sln")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "developer-cleanup-tests", Guid.NewGuid().ToString("N"));
        _paths = Enum.GetValues<NuGetCacheKind>()
            .Where(x => x != NuGetCacheKind.None)
            .ToDictionary(x => x, x => Path.Combine(_root, x.ToString()));

        foreach (var path in _paths.Values)
        {
            Directory.CreateDirectory(path);
        }

        _context = "test-user";
        _logger = new();
        _service = new(_ => Task.FromResult(new NuGetCacheLocations(_context, new Dictionary<NuGetCacheKind, string>(_paths))), _logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task Cancellation_preserves_partial_counts()
    {
        Write("first.dat");
        Write("second.dat");
        using var cancel = new CancellationTokenSource();
        var plan = await _service.ScanAsync(default);
        var result = await Clean(plan, progress: new CancelAfterProgress(cancel), token: cancel.Token);
        Assert.Multiple(() =>
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(5));
            Assert.That(Directory.GetFiles(_paths[NuGetCacheKind.HttpCache]).Length, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Cancelled_discovery_does_not_resolve_or_start_dotnet()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cli = new DotNetCli(
            () => throw new AssertionException("PATH must not be read after cancellation"),
            _ => throw new AssertionException("dotnet must not be searched after cancellation"),
            _ => throw new AssertionException("dotnet must not start after cancellation"));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            NuGetCacheDiscovery.DiscoverAsync(cli, _root, _context, Path.Combine(_root, "discovery.log"), cancellation.Token));
    }

    [Test]
    public async Task Changed_file_is_skipped_and_not_counted_as_deleted()
    {
        var file = Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        File.WriteAllText(file, "updated cache");
        var result = await Clean(plan);
        Assert.Multiple(() =>
        {
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(result.FreedBytes, Is.Zero);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Changed_user_or_cache_path_invalidates_entire_plan(bool changeUser)
    {
        var file = Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        if (changeUser)
        {
            _context = "other-user";
        }
        else
        {
            _paths[NuGetCacheKind.Temp] += "-changed";
        }

        var result = await Clean(plan);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(file), Is.True);
    }

    [Test]
    public void Cli_parser_requires_all_four_distinct_absolute_paths()
    {
        var output = string.Join('\n', NuGetCacheDiscovery.Names.Select(x => $"{x.Value}: {_paths[x.Key]}"));
        Assert.That(NuGetCacheDiscovery.Parse(output, "user").Paths.Count, Is.EqualTo(4));
        Assert.Throws<InvalidOperationException>(() => NuGetCacheDiscovery.Parse("http-cache: C:\\cache", "user"));
        Assert.Throws<InvalidOperationException>(() => NuGetCacheDiscovery.Parse(output + "\n" + output, "user"));
        Assert.Throws<IOException>(() => NuGetCleanupService.Normalize("C:\\cache:file"));
        Assert.Throws<IOException>(() => NuGetCleanupService.Normalize("relative"));
        Assert.Throws<IOException>(() => NuGetCleanupService.Normalize("C:\\Program Files. \\dotnet"));
    }

    [Test]
    public async Task Deletes_only_selected_previewed_files_and_leaves_new_files()
    {
        var selected = Write("old.dat");
        var other = Write("other.dat", kind: NuGetCacheKind.Temp);
        var plan = await _service.ScanAsync(default);
        var added = Write("new.dat");
        var result = await Clean(plan);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(5));
            Assert.That(File.Exists(selected), Is.False);
            Assert.That(File.Exists(other), Is.True);
            Assert.That(File.Exists(added), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Discovery_uses_shared_runner_and_rejects_truncated_output(bool truncated)
    {
        var script = Path.Combine(_root, "discovery.ps1");
        var lines = NuGetCacheDiscovery.Names.Select(x => $"'{x.Value}: {_paths[x.Key]}'");
        var body = new List<string>
        {
            "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)",
        };
        if (truncated)
        {
            body.Add("'x' * 30000");
        }

        body.AddRange(lines);
        await File.WriteAllLinesAsync(script, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var starts = 0;
        var cli = new DotNetCli(
            () => _root,
            _ => true,
            x =>
            {
                Assert.That(
                    x.ArgumentList,
                    Is.EqualTo(
                        new[]
                        {
                            "nuget", "locals", "all", "--list", "--force-english-output",
                        }));

                Assert.That(x.WorkingDirectory, Is.EqualTo(_root));
                Assert.That(x.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"], Is.EqualTo("1"));
                starts++;
                x.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                x.ArgumentList.Clear();
                foreach (var argument in new[]
                {
                    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                })
                {
                    x.ArgumentList.Add(argument);
                }

                return new()
                {
                    StartInfo = x,
                };
            });

        var log = Path.Combine(_root, "discovery.log");
        if (truncated)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => NuGetCacheDiscovery.DiscoverAsync(cli, _root, _context, log, default));
        }
        else
        {
            var locations = await NuGetCacheDiscovery.DiscoverAsync(cli, _root, _context, log, default);
            Assert.That(locations.Paths, Is.EquivalentTo(_paths));
        }

        var recorded = await File.ReadAllTextAsync(log);
        Assert.That(starts, Is.EqualTo(1));
        Assert.That(recorded, Does.Contain("Outcome: Succeeded"));
        Assert.That(recorded, Does.Contain("http-cache:"));
    }

    [Test]
    public async Task Global_packages_require_complete_package_structure()
    {
        var file = Write("package/1.0.0/lib/a.dll", kind: NuGetCacheKind.GlobalPackages);
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages).CanClean, Is.False);
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(file), Is.True);
    }

    [Test]
    public async Task Hidden_and_system_entries_are_measured_and_cleaned()
    {
        var hidden = Write("hidden.dat");
        var nested = Write("hidden-directory/system.dat");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(nested, FileAttributes.System);
        File.SetAttributes(Path.GetDirectoryName(nested)!, FileAttributes.Hidden | FileAttributes.Directory);
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache).Files, Is.EqualTo(2));
        var result = await Clean(plan);
        Assert.That(result.Deleted, Is.EqualTo(2));
        Assert.That(File.Exists(hidden) || File.Exists(nested), Is.False);
    }

    [Test]
    public async Task Locked_file_is_not_forced_or_counted()
    {
        var file = Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        using var reader = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await Clean(plan);
        Assert.That(result.Deleted, Is.Zero);
        Assert.That(result.Skipped, Is.EqualTo(1));
    }

    [Test]
    public async Task Missing_dotnet_is_reported_as_discovery_failure_without_starting_process()
    {
        var cli = new DotNetCli(() => string.Empty, _ => false, _ => throw new AssertionException("dotnet must not start"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuGetCacheDiscovery.DiscoverAsync(cli, _root, _context, Path.Combine(_root, "discovery.log"), default));

        Assert.That(error!.InnerException, Is.TypeOf<Win32Exception>());
    }

    [Test]
    public async Task Overlapping_categories_are_not_cleanable()
    {
        Write("cache.dat");
        _paths[NuGetCacheKind.Temp] = Path.Combine(_paths[NuGetCacheKind.HttpCache], "nested");
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Where(x => x.Kind is NuGetCacheKind.HttpCache or NuGetCacheKind.Temp).All(x => !x.CanClean), Is.True);
    }

    [Test]
    public async Task Package_marker_is_removed_even_when_a_payload_file_cannot_be_deleted()
    {
        Write("package/1.0.0/.nupkg.metadata", kind: NuGetCacheKind.GlobalPackages);
        Write("package/1.0.0/package.nuspec", kind: NuGetCacheKind.GlobalPackages);
        Write("package/1.0.0/package.1.0.0.nupkg", kind: NuGetCacheKind.GlobalPackages);
        Write("package/1.0.0/nested/.nupkg.metadata", kind: NuGetCacheKind.GlobalPackages);
        var file = Write("package/1.0.0/lib/a.dll", kind: NuGetCacheKind.GlobalPackages);
        var plan = await _service.ScanAsync(default);
        using var reader = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.Multiple(() =>
        {
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(result.Deleted, Is.EqualTo(4));
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.GlobalPackages], "package/1.0.0/.nupkg.metadata")), Is.False);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [Test]
    public async Task Plan_is_single_use_even_when_new_files_appear()
    {
        Write("old.dat");
        var plan = await _service.ScanAsync(default);
        await Clean(plan);
        var added = Write("new.dat");
        var result = await Clean(plan);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(added), Is.True);
    }

    [TestCase("project.csproj")]
    [TestCase("app.cs")]
    [TestCase("build.proj")]
    [TestCase("project.vcxproj")]
    public async Task Project_inside_a_cache_disables_the_category(string name)
    {
        Write("work/" + name);
        var plan = await _service.ScanAsync(default);
        Assert.That(plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache).CanClean, Is.False);
    }

    [Test]
    public async Task Protected_installation_root_is_rejected_without_scanning()
    {
        _paths[NuGetCacheKind.HttpCache] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var plan = await _service.ScanAsync(default);
        var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache);
        Assert.That(cache.Files, Is.Zero);
        Assert.That(cache.Errors, Is.Not.Empty);
    }

    [Test]
    public async Task Reparse_point_is_not_followed_during_scan_or_after_parent_replacement()
    {
        var file = Write("nested/cache.dat");
        var plan = await _service.ScanAsync(default);
        var parent = Path.GetDirectoryName(file)!;
        Directory.Move(parent, parent + "-old");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var protectedFile = Path.Combine(outside, "cache.dat");
        File.WriteAllText(protectedFile, "important");
        try
        {
            Directory.CreateSymbolicLink(parent, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Ignore($"Для создания тестовой ссылки нужны права или режим разработчика: {exception.Message}");
        }

        try
        {
            var result = await Clean(plan);
            var next = await _service.ScanAsync(default);
            Assert.Multiple(() =>
            {
                Assert.That(result.Deleted, Is.Zero);
                Assert.That(result.Errors, Is.Not.Empty);
                Assert.That(next.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache).CanClean, Is.False);
                Assert.That(File.ReadAllText(protectedFile), Is.EqualTo("important"));
            });
        }
        finally
        {
            Directory.Delete(parent);
        }
    }

    [Test]
    public async Task Replaced_root_is_rejected_by_identity()
    {
        Write("cache.dat");
        var plan = await _service.ScanAsync(default);
        Directory.Move(_paths[NuGetCacheKind.HttpCache], _paths[NuGetCacheKind.HttpCache] + "-old");
        var replacement = Write("cache.dat");
        var result = await Clean(plan);
        Assert.That(result.Deleted, Is.Zero);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(File.Exists(replacement), Is.True);
    }

    [Test]
    public async Task Long_paths_are_measured_and_cleaned_through_native_handles()
    {
        var file = Write(new string('d', 160) + "/" + new string('f', 100) + ".dat");
        Assert.That(file.Length, Is.GreaterThan(260));
        var plan = await _service.ScanAsync(default);
        var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.HttpCache);
        Assert.That(cache.Errors, Is.Empty);
        Assert.That(cache.Files, Is.EqualTo(1));
        var result = await Clean(plan);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Deleted, Is.EqualTo(1));
        Assert.That(File.Exists(file), Is.False);
        using var directory = CacheFileAccess.OpenDirectoryForDelete(Path.GetDirectoryName(file)!);
    }

    [TestCase(".git")]
    [TestCase(".git/config")]
    public async Task Repository_package_is_excluded_without_blocking_other_versions(string gitPath)
    {
        var bad = WritePackage("package", "1.0.0");
        var git = Write("package/1.0.0/" + gitPath, kind: NuGetCacheKind.GlobalPackages);
        var good = WritePackage("package", "2.0.0");
        var plan = await _service.ScanAsync(default);
        var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages);
        Assert.Multiple(() =>
        {
            Assert.That(cache.CanClean, Is.True);
            Assert.That(cache.Errors, Is.Empty);
            Assert.That(cache.Warnings.Single(), Does.Contain("исключён"));
            Assert.That(cache.ExcludedPackages, Is.EqualTo(1));
            Assert.That(cache.Files, Is.EqualTo(4));
            Assert.That(cache.Bytes, Is.EqualTo(20));
            Assert.That(_logger.Entries.Any(x => x.EventId == 1548 && x.Exception is IOException && x.Message.Contains("1.0.0")), Is.True);
            Assert.That(_logger.Entries.Count(x => x.EventId == 1549), Is.EqualTo(4));
        });
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Deleted, Is.EqualTo(4));
        Assert.That(File.Exists(good), Is.False);
        Assert.That(File.Exists(bad) && File.Exists(git), Is.True);
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(bad)!, ".nupkg.metadata")), Is.True);
    }

    [Test]
    public async Task Incomplete_package_is_excluded_while_valid_package_is_cleaned()
    {
        var bad = Write("incomplete/1.0.0/lib/a.dll", kind: NuGetCacheKind.GlobalPackages);
        var good = WritePackage("complete", "1.0.0");
        var plan = await _service.ScanAsync(default);
        var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages);
        Assert.That(cache.CanClean, Is.True);
        Assert.That(cache.ExcludedPackages, Is.EqualTo(1));
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(File.Exists(good), Is.False);
        Assert.That(File.Exists(bad), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Linked_package_is_excluded_without_following_it(bool entireVersion)
    {
        var good = WritePackage("complete", "1.0.0");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "important.dat");
        await File.WriteAllTextAsync(sentinel, "important");
        var link = Path.Combine(_paths[NuGetCacheKind.GlobalPackages], "linked", "1.0.0");
        if (!entireVersion)
        {
            WritePackage("linked", "1.0.0");
            link = Path.Combine(link, "nested");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Ignore($"Создание ссылки недоступно: {exception.Message}");
        }

        try
        {
            var sibling = WritePackage("linked", "2.0.0");
            var plan = await _service.ScanAsync(default);
            var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages);
            Assert.That(cache.CanClean, Is.True);
            Assert.That(cache.ExcludedPackages, Is.EqualTo(1));
            var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(File.Exists(good), Is.False);
            Assert.That(File.Exists(sibling), Is.False);
            Assert.That(await File.ReadAllTextAsync(sentinel), Is.EqualTo("important"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [TestCase(".git/config")]
    [TestCase("package/.git/config")]
    public async Task Repository_above_package_version_still_blocks_the_whole_category_and_is_logged(string gitPath)
    {
        var good = WritePackage("package", "1.0.0");
        Write(gitPath, kind: NuGetCacheKind.GlobalPackages);
        var plan = await _service.ScanAsync(default);
        var cache = plan.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages);
        Assert.That(cache.CanClean, Is.False);
        Assert.That(cache.Errors, Is.Not.Empty);
        Assert.That(_logger.Entries.Any(x => x.EventId == 1548 && x.Exception is IOException), Is.True);
        var result = await Clean(plan, NuGetCacheKind.GlobalPackages);
        Assert.That(result.Deleted, Is.Zero);
        Assert.That(File.Exists(good), Is.True);
    }

    [Test]
    public void Native_failure_preserves_the_windows_error_code()
    {
        var error = Assert.Throws<IOException>(() => CacheFileAccess.OpenFile(Path.Combine(_root, "missing.dat"), false));
        Assert.That(error!.InnerException, Is.TypeOf<Win32Exception>());
        Assert.That(((Win32Exception)error.InnerException!).NativeErrorCode, Is.EqualTo(2));
        Assert.That(error.Message, Does.Contain("Win32: 2"));
    }

    private string WritePackage(string id, string version)
    {
        Write($"{id}/{version}/.nupkg.metadata", kind: NuGetCacheKind.GlobalPackages);
        Write($"{id}/{version}/{id}.nuspec", kind: NuGetCacheKind.GlobalPackages);
        Write($"{id}/{version}/{id}.{version}.nupkg", kind: NuGetCacheKind.GlobalPackages);
        return Write($"{id}/{version}/payload.dat", kind: NuGetCacheKind.GlobalPackages);
    }

    private string Write(string name, string content = "cache", NuGetCacheKind kind = NuGetCacheKind.HttpCache)
    {
        var path = Path.Combine(_paths[kind], name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CleanupReport> Clean(
        NuGetCleanupPlan plan,
        NuGetCacheKind kind = NuGetCacheKind.HttpCache,
        IProgress<OperationProgress>? progress = null,
        CancellationToken token = default)
    {
        return _service.CleanAsync(plan, [kind], progress, token);
    }

    private sealed class CancelAfterProgress(CancellationTokenSource cancel) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            cancel.Cancel();
        }
    }
}
