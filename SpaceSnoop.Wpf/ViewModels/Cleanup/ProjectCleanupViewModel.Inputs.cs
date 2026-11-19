using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectCleanupViewModel
{
    partial void OnRootPathChanged(string value)
    {
        if (!_restoring)
        {
            InputChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseAsync()
    {
        if (!IsIdle)
        {
            return;
        }

        var path = _picker.PickFolder("Каталог проектов для поиска артефактов", RootPath);
        if (path is null)
        {
            return;
        }

        RootPath = path;
        await FindAsync();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddRoot()
    {
        if (!IsIdle)
        {
            return;
        }

        var path = _picker.PickFolder("Дополнительный корень проектов", RootPath);
        if (path is not null)
        {
            AdditionalRoots.Add(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AddCustomOutput()
    {
        if (!IsIdle)
        {
            return;
        }

        var path = _picker.PickFolder("Каталог нестандартных результатов сборки", RootPath);
        if (path is not null)
        {
            CustomOutputs.Add(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveRoot(string? path)
    {
        if (IsIdle && path is not null)
        {
            AdditionalRoots.Remove(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void RemoveCustomOutput(string? path)
    {
        if (IsIdle && path is not null)
        {
            CustomOutputs.Remove(path);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearRoots()
    {
        if (!IsIdle)
        {
            return;
        }

        RootPath = string.Empty;
        AdditionalRoots.Clear();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearCustomOutputs()
    {
        if (IsIdle)
        {
            CustomOutputs.Clear();
        }
    }

    private void InputChanged()
    {
        if (_restoring)
        {
            return;
        }

        _inputVersion++;
        _plan = null;
        ResultText = null;
        ClearRows();
        PersistInputs();
        StatusText = "Пути изменились. Повторите поиск.";
        FindCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanClean));
    }

    private string[] ScopeRoots()
    {
        return new[]
            {
                RootPath,
            }.Concat(AdditionalRoots)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private bool SameInputs(IReadOnlyCollection<string> roots, IReadOnlyCollection<string> outputs)
    {
        return roots.SequenceEqual(ScopeRoots(), StringComparer.Ordinal)
            && outputs.SequenceEqual(CustomOutputs, StringComparer.Ordinal);
    }

    private void RestoreInputs()
    {
        _restoring = true;
        try
        {
            var roots = ReadPaths(_settings.GetStringValue(SettingsKeys.DeveloperProjectRoots));
            RootPath = roots.FirstOrDefault() ?? string.Empty;
            foreach (var path in roots.Skip(1))
            {
                AdditionalRoots.Add(path);
            }

            foreach (var path in ReadPaths(_settings.GetStringValue(SettingsKeys.DeveloperCustomOutputs)))
            {
                CustomOutputs.Add(path);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    private void PersistInputs()
    {
        _settings.SetValue(SettingsKeys.DeveloperProjectRoots, JsonSerializer.Serialize(ScopeRoots()));
        _settings.SetValue(SettingsKeys.DeveloperCustomOutputs, JsonSerializer.Serialize(CustomOutputs));
    }

    private static string[] ReadPaths(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
