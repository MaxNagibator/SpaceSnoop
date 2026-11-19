namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectCleanupViewModel
{
    internal void ShowForAutomation()
    {
        ResetForAutomation();
        _restoring = true;
        try
        {
            RootPath = @"C:\Sources\Projects";
            AdditionalRoots.Add(@"C:\Sources\Samples");
            CustomOutputs.Add(@"C:\Sources\Projects\Legacy\artifacts");
        }
        finally
        {
            _restoring = false;
        }

        _dispatcher.Invoke(() =>
        {
            foreach (var row in new[]
            {
                new ProjectArtifactViewModel(
                    ProjectArtifactKind.Bin,
                    @"C:\Sources\Projects\App\bin",
                    840_000_000,
                    320,
                    true,
                    string.Empty,
                    @"C:\Sources\Projects\App",
                    [new(@"C:\Sources\Projects\App\bin\Debug\app.dll", 840_000_000)],
                    projectBytes: 960_000_000),
                new ProjectArtifactViewModel(
                    ProjectArtifactKind.Obj,
                    @"C:\Sources\Projects\App\obj",
                    120_000_000,
                    640,
                    true,
                    string.Empty,
                    @"C:\Sources\Projects\App",
                    [new(@"C:\Sources\Projects\App\obj\Debug\data.bin", 120_000_000)],
                    projectBytes: 960_000_000),
                new ProjectArtifactViewModel(
                    ProjectArtifactKind.Coverage,
                    @"C:\Sources\Projects\App.Tests\TestResults\coverage.cobertura.xml",
                    64_000_000,
                    1,
                    true,
                    string.Empty,
                    @"C:\Sources\Projects\App.Tests",
                    [new(@"C:\Sources\Projects\App.Tests\TestResults\coverage.cobertura.xml", 64_000_000)]),
                new ProjectArtifactViewModel(
                    ProjectArtifactKind.CustomOutput,
                    @"C:\Sources\Projects\Legacy\artifacts",
                    8_000_000,
                    12,
                    true,
                    string.Empty,
                    @"C:\Sources\Projects\Legacy",
                    [new(@"C:\Sources\Projects\Legacy\artifacts\report.json", 8_000_000)]),
            })
            {
                row.PropertyChanged += OnSelectionChanged;
                Artifacts.Add(row);
            }

            RowsView.Refresh();
            StatusText = "Пример результатов поиска. Очистка в демонстрации недоступна.";
            NotifySelection();
        });
    }

    internal void ResetForAutomation()
    {
        _plan = null;
        _inputVersion++;
        _restoring = true;
        try
        {
            RootPath = string.Empty;
            AdditionalRoots.Clear();
            CustomOutputs.Clear();
        }
        finally
        {
            _restoring = false;
        }

        ResultText = null;
        StatusText = null;
        ClearRows();
    }
}
