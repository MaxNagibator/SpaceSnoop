using KeepShell.Services;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncQuickProfilesViewModel(
    ISettingsStore settings,
    IDialogService dialogs,
    Func<string, string, SyncProfile?, SyncProfile> capture,
    Action<SyncProfile> apply,
    Func<bool> canSave,
    Action<string> setStatus)
    : ObservableObject
{
    public const string CurrentProfileId = "__current";

    private bool _loading;
    private bool _applying;

    [ObservableProperty]
    private SyncQuickProfileItem? _selectedProfile;

    public RangeObservableCollection<SyncQuickProfileItem> Items { get; } = [];

    public void Load(string? selectedId = null)
    {
        selectedId ??= SelectedProfile?.Id;

        var profiles = SyncProfileStore.Load(settings)
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(profile => new SyncQuickProfileItem(profile,
                false,
                RequestDeleteProfile,
                ConfirmDeleteProfile,
                CancelDeleteProfile,
                RequestUpdateProfile,
                ConfirmUpdateProfile,
                CancelUpdateProfile,
                RequestRenameProfile,
                ConfirmRenameProfile,
                CancelRenameProfile))
            .ToList();

        profiles.Insert(0,
            new(capture(CurrentProfileId, "Текущие пути", null),
                true,
                RequestDeleteProfile,
                ConfirmDeleteProfile,
                CancelDeleteProfile,
                RequestUpdateProfile,
                ConfirmUpdateProfile,
                CancelUpdateProfile,
                RequestRenameProfile,
                ConfirmRenameProfile,
                CancelRenameProfile));

        _loading = true;
        Items.ReplaceAll(profiles);
        SelectedProfile = Items.FirstOrDefault(profile => string.Equals(profile.Id, selectedId, StringComparison.Ordinal))
                          ?? Items.FirstOrDefault();

        _loading = false;
    }

    public void MarkCurrent()
    {
        if (_loading || _applying || Items.Count == 0)
        {
            return;
        }

        var current = capture(CurrentProfileId, "Текущие пути", null);
        var model = Items[0].Model;
        model.Left = current.Left;
        model.Right = current.Right;
        model.Mode = current.Mode;
        model.Mirror = current.Mirror;
        model.Winner = current.Winner;
        model.Exclusions = current.Exclusions;
        SelectedProfile = Items[0];
    }

    public void NotifyCanSaveChanged()
    {
        SaveProfileCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private static void CancelUpdateProfile(SyncQuickProfileItem profile)
    {
        profile.IsUpdateConfirming = false;
    }

    [RelayCommand]
    private static void CancelRenameProfile(SyncQuickProfileItem profile)
    {
        profile.IsRenameConfirming = false;
    }

    [RelayCommand]
    private static void CancelDeleteProfile(SyncQuickProfileItem profile)
    {
        profile.IsDeleteConfirming = false;
    }

    private static string BuildProfileName(string left, string right)
    {
        return $"{PathName(left)} → {PathName(right)}";

        static string PathName(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }
    }

    partial void OnSelectedProfileChanged(SyncQuickProfileItem? value)
    {
        if (_loading || value is null || value.IsDefault)
        {
            return;
        }

        _applying = true;

        try
        {
            apply(value.Model);
        }
        finally
        {
            _applying = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private void SaveProfile()
    {
        if (!TryBuildProfile(null, string.Empty, out var profile))
        {
            return;
        }

        var profiles = SyncProfileStore.Load(settings);
        profiles.Add(profile);
        SyncProfileStore.Save(settings, profiles);
        Load(profile.Id);
        setStatus($"Профиль сохранён: {profile.Name}.");
    }

    private bool CanSaveProfile()
    {
        return canSave();
    }

    [RelayCommand]
    private void RequestUpdateProfile(SyncQuickProfileItem profile)
    {
        foreach (var item in Items)
        {
            item.IsDeleteConfirming = false;
            item.IsUpdateConfirming = false;
            item.IsRenameConfirming = false;
        }

        profile.IsUpdateConfirming = true;
    }

    [RelayCommand]
    private void ConfirmUpdateProfile(SyncQuickProfileItem profile)
    {
        if (!TryBuildProfile(profile.Model, profile.Model.Name, out var updated))
        {
            return;
        }

        var profiles = SyncProfileStore.Load(settings);
        var index = profiles.FindIndex(model => string.Equals(model.Id, profile.Id, StringComparison.Ordinal));

        if (index < 0)
        {
            profiles.Add(updated);
        }
        else
        {
            profiles[index] = updated;
        }

        SyncProfileStore.Save(settings, profiles);
        Load(updated.Id);
        setStatus($"Профиль обновлён: {updated.Name}.");
    }

    [RelayCommand]
    private void RequestRenameProfile(SyncQuickProfileItem profile)
    {
        foreach (var item in Items)
        {
            item.IsDeleteConfirming = false;
            item.IsUpdateConfirming = false;
            item.IsRenameConfirming = false;
        }

        profile.EditName = profile.Name;
        profile.IsRenameConfirming = true;
    }

    [RelayCommand]
    private void ConfirmRenameProfile(SyncQuickProfileItem profile)
    {
        var profiles = SyncProfileStore.Load(settings);
        var index = profiles.FindIndex(model => string.Equals(model.Id, profile.Id, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        var updated = profiles[index];
        updated.Name = profile.EditName.Trim();

        if (string.IsNullOrWhiteSpace(updated.Name))
        {
            updated.Name = BuildProfileName(updated.Left, updated.Right);
        }

        SyncProfileStore.Save(settings, profiles);
        Load(updated.Id);
        setStatus($"Профиль переименован: {updated.Name}.");
    }

    [RelayCommand]
    private void RequestDeleteProfile(SyncQuickProfileItem profile)
    {
        foreach (var item in Items)
        {
            item.IsDeleteConfirming = false;
            item.IsUpdateConfirming = false;
            item.IsRenameConfirming = false;
        }

        profile.IsDeleteConfirming = true;
    }

    [RelayCommand]
    private void ConfirmDeleteProfile(SyncQuickProfileItem profile)
    {
        var profiles = SyncProfileStore.Load(settings);
        profiles.RemoveAll(model => string.Equals(model.Id, profile.Id, StringComparison.Ordinal));
        SyncProfileStore.Save(settings, profiles);
        SyncScheduler.Remove(SyncScheduler.TaskNameFor(profile.Id), out _);
        Load();
        setStatus($"Профиль удалён: {profile.Name}.");
    }

    private bool TryBuildProfile(SyncProfile? existing, string name, out SyncProfile profile)
    {
        var candidate = capture(existing?.Id ?? Guid.NewGuid().ToString("N")[..8],
            name.Length > 0 ? name : string.Empty,
            existing);

        var left = candidate.Left;
        var right = candidate.Right;

        if (left.Length == 0 || right.Length == 0)
        {
            dialogs.Warning("Профиль синхронизации", "Укажите оба каталога.");
            profile = new();
            return false;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            dialogs.Warning("Профиль синхронизации", "Каталоги совпадают или вложены друг в друга – такой профиль опасен.");
            profile = new();
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidate.Name))
        {
            candidate.Name = BuildProfileName(left, right);
        }

        profile = candidate;
        return true;
    }
}
