using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Tests;

[TestFixture]
public sealed class ProjectCleanupTests
{
    private string _root = null!;
    private ProjectCleanupService _service = null!;
    private List<string> _recycled = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "SpaceSnoop.sln")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "project-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _recycled = [];
        _service = new(_recycled.Add);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task Cancel_during_failed_last_recycle_restores_files_and_retains_error()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        using var cancellation = new CancellationTokenSource();
        _service = new(_ =>
        {
            cancellation.Cancel();
            throw new IOException("Recycle failed");
        });

        var plan = await _service.ScanAsync(_root, default);
        var result = await _service.CleanAsync(plan, [plan.Artifacts.Single().Id], null, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(result.FreedBytes, Is.Zero);
            Assert.That(result.Errors, Is.EqualTo(new[] { "Recycle failed" }));
            Assert.That(File.ReadAllText(file), Is.EqualTo("data"));
            Assert.That(Directory.EnumerateDirectories(_root, ".SpaceSnoop-*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public async Task Cancel_during_last_recycle_preserves_completed_counts_and_reports_cancellation()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        using var cancellation = new CancellationTokenSource();
        _service = new(path =>
        {
            _recycled.Add(path);
            cancellation.Cancel();
        });

        var plan = await _service.ScanAsync(_root, default);
        var result = await _service.CleanAsync(plan, [plan.Artifacts.Single().Id], null, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(4));
            Assert.That(result.Errors, Is.Empty);
            Assert.That(File.Exists(file), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_recycled.Single(), "files", "app.dll")), Is.EqualTo("data"));
        });
    }

    [Test]
    public async Task Cancel_during_staging_restores_originals()
    {
        Write("app/app.csproj");
        var first = Write("app/bin/first.dll");
        var second = Write("app/bin/second.dll");
        var plan = await _service.ScanAsync(_root, default);
        using var cancellation = new CancellationTokenSource();
        var result = await _service.CleanAsync(
            plan,
            [plan.Artifacts.Single().Path],
            new CancelProgress(cancellation),
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.Cancelled, Is.True);
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(File.Exists(first) && File.Exists(second), Is.True);
            Assert.That(_recycled, Is.Empty);
        });
    }

    [Test]
    public async Task Changed_files_beyond_report_ceiling_are_counted_but_not_logged()
    {
        const int count = 60;
        var logger = new CapturingLogger<ProjectCleanupService>();
        var service = new ProjectCleanupService(_ => Assert.Fail("nothing to recycle"), logger);
        Write("app/app.csproj");
        var files = Enumerable.Range(0, count).Select(x => Write($"app/bin/{x}.dll")).ToArray();
        var plan = await service.ScanAsync(_root, default);
        foreach (var file in files)
        {
            File.WriteAllText(file, "changed after preview");
        }

        var result = await service.CleanAsync(plan, plan.Artifacts.Where(x => x.Kind == ProjectArtifactKind.Bin).Select(x => x.Id).ToArray(), null, default);
        Assert.Multiple(() =>
        {
            Assert.That(result.Skipped, Is.EqualTo(count));
            Assert.That(result.Errors, Has.Count.EqualTo(CleanupDefaults.MaxReportedErrors));
            Assert.That(logger.Entries.Count(x => x.EventId == 1534), Is.EqualTo(CleanupDefaults.MaxReportedErrors));
            Assert.That(logger.Entries.Select(x => x.EventId), Has.None.InRange(1540, 1543));
        });
    }

    [Test]
    public async Task Coverage_outside_application_directory_is_not_blocked_by_its_group_directory()
    {
        Write("app.csproj");
        Write("coverage.xml");
        Write("bin/app.dll");
        Write("bin/coverage.json");
        var plan = await _service.ScanAsync(_root, default);
        var appDirectory = Path.Combine(_root, "bin");
        Assert.Multiple(() =>
        {
            Assert.That(
                ProjectCleanupService.ContainsApplicationFiles(plan.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Coverage && x.Path == _root), appDirectory),
                Is.False);

            Assert.That(
                plan.Artifacts.Where(x => x.Path == appDirectory)
                    .All(x => ProjectCleanupService.ContainsApplicationFiles(x, appDirectory)),
                Is.True);
        });
    }

    [Test]
    public async Task Custom_output_containing_running_application_is_not_measured()
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName;
        var plan = await _service.ScanAsync([], [parent], default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Artifacts, Is.Empty);
            Assert.That(plan.Errors.Any(x => x.Contains("содержит запущенное приложение")), Is.True);
        });
    }

    [Test]
    public async Task Custom_output_inside_project_is_not_scanned_twice_and_coverage_stays_separate()
    {
        Write("app.csproj");
        Write("coverage.xml");
        Write("bin/app.dll");
        var custom = Write("build-output/data.bin");
        var coverage = Write("build-output/coverage.json");
        var plan = await _service.ScanAsync([_root], [Path.GetDirectoryName(custom)!], default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Artifacts.Sum(x => x.Files), Is.EqualTo(4));
            Assert.That(plan.Artifacts.SelectMany(x => x.Contents).Select(x => x.Path).Distinct().Count(), Is.EqualTo(4));
        });

        var report = await Clean(plan, ProjectArtifactKind.CustomOutput);
        Assert.Multiple(() =>
        {
            Assert.That(report.Errors, Is.Empty);
            Assert.That(File.Exists(custom), Is.False);
            Assert.That(File.Exists(coverage), Is.True);
        });
    }

    [Test]
    public async Task Custom_output_with_project_sources_is_not_available_for_cleanup()
    {
        Write("output/project.csproj");
        var file = Write("output/data.bin");
        var plan = await _service.ScanAsync([], [Path.GetDirectoryName(file)!], default);
        Assert.That(plan.Artifacts.Single().CanClean, Is.False);
        var report = await Clean(plan, ProjectArtifactKind.CustomOutput);
        Assert.Multiple(() =>
        {
            Assert.That(report.Errors, Is.Not.Empty);
            Assert.That(File.Exists(file), Is.True);
            Assert.That(_recycled, Is.Empty);
        });
    }

    [Test]
    public async Task Custom_parent_of_search_root_is_rejected_without_hiding_standard_artifacts()
    {
        Write("app/app.csproj");
        Write("app/bin/app.dll");
        var plan = await _service.ScanAsync([Path.Combine(_root, "app")], [_root], default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors.Any(x => x.Contains("содержит корень поиска")), Is.True);
            Assert.That(plan.Artifacts.Single().Kind, Is.EqualTo(ProjectArtifactKind.Bin));
            Assert.That(plan.Artifacts.Single().Files, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Every_root_identity_is_checked_before_any_selected_target_is_moved()
    {
        Write("a/a.csproj");
        var first = Write("a/bin/a.dll");
        Write("b/b.csproj");
        Write("b/bin/b.dll");
        var secondRoot = Path.Combine(_root, "b");
        var plan = await _service.ScanAsync([Path.Combine(_root, "a"), secondRoot], [], default);
        Directory.Move(secondRoot, secondRoot + "-original");
        Directory.CreateDirectory(secondRoot);
        var report = await _service.CleanAsync(plan, [plan.Artifacts.Single(x => x.ProjectDirectory.EndsWith("a")).Id], null, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Errors.Any(x => x.Contains("корень заменён")), Is.True);
            Assert.That(File.Exists(first), Is.True);
            Assert.That(_recycled, Is.Empty);
        });
    }

    [Test]
    public async Task Explicit_custom_output_can_be_the_only_root_and_new_files_stay_out_of_the_recycle_batch()
    {
        var file = Write("output/data.bin");
        var output = Path.GetDirectoryName(file)!;
        var plan = await _service.ScanAsync([], [output, output], default);
        var newFile = Write("output/new.bin");
        Assert.Multiple(() =>
        {
            Assert.That(
                plan.CustomOutputs,
                Is.EqualTo(
                    new[]
                    {
                        output,
                    }));

            Assert.That(plan.Artifacts.Single().Kind, Is.EqualTo(ProjectArtifactKind.CustomOutput));
            Assert.That(plan.Artifacts.Single().Contents.Single().Path, Is.EqualTo(Path.GetFullPath(file)));
        });

        var report = await Clean(plan, ProjectArtifactKind.CustomOutput);
        Assert.Multiple(() =>
        {
            Assert.That(report.Errors, Is.Empty);
            Assert.That(File.Exists(file), Is.False);
            Assert.That(File.Exists(newFile), Is.True);
            Assert.That(_recycled.Single(), Does.StartWith(output + Path.DirectorySeparatorChar));
        });
    }

    [Test]
    public async Task Failed_recycle_restores_files_without_overwriting_new_files()
    {
        Write("app/app.csproj");
        var source = Write("app/bin/app.dll");
        _service = new(_ =>
        {
            File.WriteAllText(source, "new build");
            throw new IOException("Recycle Bin unavailable");
        });

        var plan = await _service.ScanAsync(_root, default);
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(result.Errors.Any(x => x.Contains("восстановления")), Is.True);
            Assert.That(File.ReadAllText(source), Is.EqualTo("new build"));
            Assert.That(
                Directory.GetFiles(_root, "app.dll", SearchOption.AllDirectories)
                    .Any(x => File.ReadAllText(x) == "data"),
                Is.True);
        });
    }

    [Test]
    public async Task Finds_only_project_outputs_and_separates_nested_results()
    {
        Write("app/app.csproj");
        Write("app/bin/Debug/app.dll");
        Write("app/bin/Debug/TestResults/test.trx");
        Write("app/obj/project.assets.json");
        Write("app/BenchmarkDotNet.Artifacts/report.md");
        Write("other/bin/important.txt");
        Write(".git/inner/app.csproj");
        Write(".git/inner/bin/important.txt");
        Write("app/publish/app.dll");
        var plan = await _service.ScanAsync(_root, default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(
                plan.Artifacts.Select(x => x.Kind),
                Is.EquivalentTo(
                    new[]
                    {
                        ProjectArtifactKind.Bin, ProjectArtifactKind.Obj, ProjectArtifactKind.TestResults, ProjectArtifactKind.Benchmarks,
                    }));

            Assert.That(plan.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin).Files, Is.EqualTo(1));
            Assert.That(plan.Artifacts.All(x => x.CanClean), Is.True);
        });
    }

    [Test]
    public async Task Foreign_used_and_forged_targets_are_rejected()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        var plan = await _service.ScanAsync(_root, default);
        var foreign = await new ProjectCleanupService(_ => Assert.Fail("foreign recycle")).CleanAsync(
            plan,
            [plan.Artifacts.Single().Path],
            null,
            default);

        var forged = await _service.CleanAsync(await _service.ScanAsync(_root, default), [Path.GetDirectoryName(file)! + "-other"], null, default);
        await _service.CleanAsync(plan, ["invalid"], null, default);
        var used = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(foreign.Errors, Is.Not.Empty);
            Assert.That(forged.Errors, Is.Not.Empty);
            Assert.That(used.Errors, Is.Not.Empty);
            Assert.That(File.Exists(file), Is.True);
        });
    }

    [Test]
    public async Task Legacy_path_selection_cannot_select_both_build_files_and_coverage_with_one_ambiguous_path()
    {
        Write("app.csproj");
        var assembly = Write("bin/app.dll");
        var coverage = Write("bin/coverage.json");
        var plan = await _service.ScanAsync(_root, default);
        var report = await _service.CleanAsync(plan, [Path.GetDirectoryName(assembly)!], null, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Errors, Is.Not.Empty);
            Assert.That(File.Exists(assembly) && File.Exists(coverage), Is.True);
            Assert.That(_recycled, Is.Empty);
        });
    }

    [Test]
    public async Task Link_in_output_disables_category_and_root_links_are_rejected()
    {
        Write("app/app.csproj");
        Write("app/bin/app.dll");
        var outside = Write("outside/important.txt");
        var link = Path.Combine(_root, "app", "bin", "linked");
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!);
        }
        catch (Exception exception) when (IsSymlinkUnavailable(exception))
        {
            Assert.Ignore($"Создание ссылки недоступно: {exception.Message}");
        }
        try
        {
            var plan = await _service.ScanAsync(_root, default);
            Assert.That(plan.Artifacts.Single().CanClean, Is.False);
            await Assert.ThrowsAsync<IOException>(() => _service.ScanAsync(link, default));
            Assert.That(File.ReadAllText(outside), Is.EqualTo("data"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Test]
    public async Task Native_recycler_moves_own_fixture_to_Windows_Recycle_Bin()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        _service = new();
        var plan = await _service.ScanAsync(_root, default);
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(File.Exists(file), Is.False);
            Assert.That(Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(file)!)!, ".SpaceSnoop-*"), Is.Empty);
        });
    }

    [Test]
    [NonParallelizable]
    public async Task Nested_dotnet_installation_disables_custom_output_cleanup()
    {
        var file = Write("output/dotnet/sdk/version.dat");
        Write("output/result.bin");
        var previous = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.Combine(_root, "output", "dotnet"));
            var plan = await _service.ScanAsync([], [Path.Combine(_root, "output")], default);
            Assert.That(plan.Artifacts.Single().CanClean, Is.False);
            var report = await Clean(plan, ProjectArtifactKind.CustomOutput);
            Assert.Multiple(() =>
            {
                Assert.That(report.Errors, Is.Not.Empty);
                Assert.That(File.Exists(file), Is.True);
                Assert.That(_recycled, Is.Empty);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", previous);
        }
    }

    [Test]
    public async Task Nested_report_keeps_its_nearest_project_group()
    {
        Write("app/project.vbproj");
        var report = Write("app/reports/run/lcov.info");
        var plan = await _service.ScanAsync(_root, default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Artifacts.Single().ProjectDirectory, Is.EqualTo(Path.Combine(_root, "app")));
            Assert.That(plan.Artifacts.Single().Contents.Single().Path, Is.EqualTo(Path.GetFullPath(report)));
        });
    }

    [Test]
    public async Task Overlapping_roots_are_scanned_once_and_grouped_by_project()
    {
        Write("a/a.csproj");
        Write("a/bin/a.dll");
        Write("b/b.fsproj");
        Write("b/obj/b.dll");
        var plan = await _service.ScanAsync([Path.Combine(_root, "a"), _root, Path.Combine(_root, "b"), _root], [], default);
        Assert.Multiple(() =>
        {
            Assert.That(
                plan.Roots,
                Is.EqualTo(
                    new[]
                    {
                        _root,
                    }));

            Assert.That(plan.Artifacts.Sum(x => x.Files), Is.EqualTo(2));
            Assert.That(
                plan.Artifacts.Select(x => x.ProjectDirectory),
                Is.EquivalentTo(
                    new[]
                    {
                        Path.Combine(_root, "a"), Path.Combine(_root, "b"),
                    }));
        });
    }

    [Test]
    public async Task Recovery_path_is_reported_even_after_many_file_errors()
    {
        Write("app/app.csproj");
        var blocked = Enumerable.Range(0, 50).Select(x => Write($"app/bin/locked-{x}.dll")).ToArray();
        var source = Write("app/bin/app.dll");
        _service = new(_ =>
        {
            File.WriteAllText(source, "new build");
            throw new IOException("Recycle Bin unavailable");
        });

        var plan = await _service.ScanAsync(_root, default);
        var handles = blocked.Select(x => File.Open(x, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToArray();
        try
        {
            var result = await Clean(plan, ProjectArtifactKind.Bin);
            Assert.That(
                result.Errors.Any(x => x.Contains("Файлы сохранены для восстановления")),
                Is.True,
                "Ошибки занятых файлов скрыли путь оставшегося файла.");

            Assert.That(File.ReadAllText(source), Is.EqualTo("new build"));
        }
        finally
        {
            foreach (var handle in handles)
            {
                handle.Dispose();
            }
        }
    }

    [Test]
    public async Task Recycles_only_selected_previewed_unchanged_files()
    {
        Write("app/app.csproj");
        var old = Write("app/bin/old.dll");
        var changed = Write("app/bin/changed.dll");
        var other = Write("app/obj/data.cache");
        var plan = await _service.ScanAsync(_root, default);
        var added = Write("app/bin/new.dll");
        File.WriteAllText(changed, "changed after preview");
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Has.Count.EqualTo(1), string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.Errors, Has.Some.Contains(Path.GetFullPath(changed)).And.Contains("файл изменился после поиска"));
            Assert.That(result.Deleted, Is.EqualTo(1));
            Assert.That(result.FreedBytes, Is.EqualTo(4));
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(File.Exists(old), Is.False);
            Assert.That(File.Exists(changed) && File.Exists(added) && File.Exists(other), Is.True);
            Assert.That(_recycled, Has.Count.EqualTo(1));
        });

        Assert.That(File.ReadAllText(Path.Combine(_recycled.Single(), "files", "old.dll")), Is.EqualTo("data"));
        Assert.That(File.Exists(Path.Combine(_recycled.Single(), "SpaceSnoop-restore.json")), Is.True);
    }

    [Test]
    public async Task Replaced_output_directory_is_not_recycled()
    {
        Write("app/app.csproj");
        var file = Write("app/bin/app.dll");
        var plan = await _service.ScanAsync(_root, default);
        Directory.Move(Path.GetDirectoryName(file)!, Path.Combine(_root, "saved-bin"));
        Write("app/bin/app.dll", "replacement");
        var result = await Clean(plan, ProjectArtifactKind.Bin);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.Deleted, Is.Zero);
            Assert.That(File.ReadAllText(file), Is.EqualTo("replacement"));
        });
    }

    [Test]
    public async Task Standalone_coverage_has_distinct_selection_and_never_double_counts_build_or_test_results()
    {
        Write("app.csproj");
        var rootCoverage = Write("coverage.cobertura.xml");
        var binCoverage = Write("bin/coverage.json");
        var assembly = Write("bin/app.dll");
        var testCoverage = Write("TestResults/coverage.xml");
        var note = Write("notes.xml");
        var plan = await _service.ScanAsync(_root, default);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Artifacts.Count(x => x.Kind == ProjectArtifactKind.Coverage), Is.EqualTo(2));
            Assert.That(plan.Artifacts.Sum(x => x.Files), Is.EqualTo(4));
            Assert.That(plan.Artifacts.SelectMany(x => x.Contents).Select(x => x.Path).Distinct().Count(), Is.EqualTo(4));
            Assert.That(plan.Artifacts.Select(x => x.Id).Distinct().Count(), Is.EqualTo(4));
        });

        var report = await Clean(plan, ProjectArtifactKind.Coverage);
        Assert.Multiple(() =>
        {
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.Deleted, Is.EqualTo(2));
            Assert.That(File.Exists(rootCoverage) || File.Exists(binCoverage), Is.False);
            Assert.That(File.Exists(assembly) && File.Exists(testCoverage) && File.Exists(note), Is.True);
            Assert.That(_recycled.All(path => path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task Volume_and_home_roots_are_rejected()
    {
        await Assert.ThrowsAsync<IOException>(() => _service.ScanAsync(Path.GetPathRoot(_root)!, default));
        await Assert.ThrowsAsync<IOException>(() => _service.ScanAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), default));
    }

    private static bool IsSymlinkUnavailable(Exception exception)
    {
        return exception is UnauthorizedAccessException
            || (exception is IOException
                && (unchecked((uint)exception.HResult) >> 16) == 0x8007
                && (exception.HResult & 0xFFFF) is 1 or 5 or 50 or 1314);
    }

    private string Write(string relative, string content = "data")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private Task<CleanupReport> Clean(ProjectCleanupPlan plan, params ProjectArtifactKind[] kinds)
    {
        return _service.CleanAsync(plan, plan.Artifacts.Where(x => kinds.Contains(x.Kind)).Select(x => x.Id).ToArray(), null, default);
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            cancellation.Cancel();
        }
    }
}
