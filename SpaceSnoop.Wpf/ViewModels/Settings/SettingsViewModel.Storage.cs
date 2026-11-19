using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class SettingsViewModel
{
    public string SettingsFilePath => _settings.FilePath;

    public string DataDirectory => AppStorage.DataDirectory;

    public bool StoreInAppData
    {
        get => AppStorage.UseAppData;
        set
        {
            if (value != AppStorage.UseAppData)
            {
                _ = ChangeStorageLocationAsync(value)
                    .ContinueWith(
                        task => _logger.StorageLocationChangeFailed(task.Exception!, AppStorage.DirectoryFor(value)),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }

            OnPropertyChanged();
        }
    }

    private async Task ChangeStorageLocationAsync(bool useAppData)
    {
        var source = AppStorage.DataDirectory;
        var destination = AppStorage.DirectoryFor(useAppData);
        var place = useAppData ? "в папке AppData" : "рядом с программой";

        var migrate = new ConfirmChoice("Перенести файлы", ConfirmChoiceKind.Primary);

        var confirm = new ConfirmDialogViewModel(
            "Расположение данных",
            PackIconLucideKind.HardDrive,
            [
                new ConfirmTextLine($"Файлы приложения будут храниться {place}:"),
                new ConfirmTextLine(destination, ConfirmTextTone.Muted),
                new ConfirmGapLine(),
                new ConfirmTextLine("Текущие настройки, логи и журналы можно перенести туда или оставить на старом месте."),
                new ConfirmTextLine("После смены приложение перезапустится."),
            ],
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Оставить файлы на месте", ConfirmChoiceKind.Secondary),
                migrate,
            ]);

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        try
        {
            _settings.Flush();

            var migrateChosen = ReferenceEquals(confirm.Chosen, migrate);

            if (migrateChosen)
            {
                var left = AppStorage.Migrate(source, destination);

                if (left > 0)
                {
                    _logger.StorageSourceFilesLeft(left);
                }
            }

            AppStorage.SetUseAppData(useAppData, migrateChosen);
            _logger.StorageLocationChanged(destination);
        }
        catch (Exception exception)
        {
            _logger.StorageLocationChangeFailed(exception, destination);
            _dialogs.Error("Расположение данных", $"Не удалось изменить расположение данных.{Environment.NewLine}{Environment.NewLine}{exception.Message}");

            return;
        }

        _settings.DiscardPending();
        _settings.Close();

        var executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable) || !_shell.Start(executable))
        {
            _dialogs.Error("Расположение данных", "Данные перенесены, но перезапустить программу не удалось – закройте и откройте её сами.");

            return;
        }

        _lifetime.Shutdown();
    }

    [RelayCommand]
    private void OpenSettingsFile()
    {
        try
        {
            _settings.Flush();
        }
        catch (Exception exception)
        {
            _logger.SettingsFlushFailed(exception, SettingsFilePath);
        }

        _shell.Reveal(SettingsFilePath);
    }

    [RelayCommand]
    private void CopySettingsPath()
    {
        _clipboard.TrySetText(SettingsFilePath);
    }
}
