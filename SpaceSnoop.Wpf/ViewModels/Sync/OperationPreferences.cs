using System.IO.Compression;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class OperationPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private bool _confirmBeforeDelete = AppDefaults.DeleteConfirmDefault;

    [ObservableProperty]
    private DeleteMode _deleteMode = AppDefaults.DeleteModeDefault;

    [ObservableProperty]
    private string _defaultExclusions = string.Empty;

    [ObservableProperty]
    private bool _syncPathSuggest = AppDefaults.SyncPathSuggestDefault;

    [ObservableProperty]
    private string _groupFolders = AppDefaults.SyncGroupFoldersDefault;

    [ObservableProperty]
    private bool _recycleOverwritten = AppDefaults.SyncRecycleOverwrittenDefault;

    [ObservableProperty]
    private bool _deleteOriginalAfterArchive = AppDefaults.ArchiveDeleteOriginalDefault;

    [ObservableProperty]
    private CompressionLevel _archiveCompression = AppDefaults.ArchiveCompressionDefault;

    public OperationPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        ConfirmBeforeDelete = _settings.GetBool(SettingsKeys.DeleteConfirm, AppDefaults.DeleteConfirmDefault);
        DeleteMode = _settings.GetEnum(SettingsKeys.DeleteMode, AppDefaults.DeleteModeDefault);
        DefaultExclusions = _settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty;
        SyncPathSuggest = _settings.GetBool(SettingsKeys.SyncPathSuggest, AppDefaults.SyncPathSuggestDefault);
        GroupFolders = _settings.GetStringValue(SettingsKeys.SyncGroupFolders) ?? AppDefaults.SyncGroupFoldersDefault;
        RecycleOverwritten = _settings.GetBool(SettingsKeys.SyncRecycleOverwritten, AppDefaults.SyncRecycleOverwrittenDefault);
        DeleteOriginalAfterArchive = _settings.GetBool(SettingsKeys.ArchiveDeleteOriginal, AppDefaults.ArchiveDeleteOriginalDefault);
        ArchiveCompression = _settings.GetEnum(SettingsKeys.ArchiveCompression, AppDefaults.ArchiveCompressionDefault);
        _suppressPersist = false;
    }

    partial void OnConfirmBeforeDeleteChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.DeleteConfirm, value);
        }
    }

    partial void OnDeleteModeChanged(DeleteMode value)
    {
        if (!_suppressPersist)
        {
            _settings.SetEnum(SettingsKeys.DeleteMode, value);
        }
    }

    partial void OnDefaultExclusionsChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.DefaultExclusions, value);
        }
    }

    partial void OnSyncPathSuggestChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.SyncPathSuggest, value);
        }
    }

    partial void OnGroupFoldersChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.SyncGroupFolders, value);
        }
    }

    partial void OnRecycleOverwrittenChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.SyncRecycleOverwritten, value);
        }
    }

    partial void OnDeleteOriginalAfterArchiveChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ArchiveDeleteOriginal, value);
        }
    }

    partial void OnArchiveCompressionChanged(CompressionLevel value)
    {
        if (!_suppressPersist)
        {
            _settings.SetEnum(SettingsKeys.ArchiveCompression, value);
        }
    }
}
