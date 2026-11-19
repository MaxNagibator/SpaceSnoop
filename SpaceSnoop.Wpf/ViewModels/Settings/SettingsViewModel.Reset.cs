using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class SettingsViewModel
{
    [RelayCommand(CanExecute = nameof(CanResetSection))]
    private void ResetSection(string? sectionKey)
    {
        if (sectionKey is null || !ResetPlans.TryGetValue(sectionKey, out var plan) || !plan.CanReset)
        {
            return;
        }

        plan.Reset();
        NotifyDerivedOptions();
        RefreshResetAvailability();

        var title = Sections[sectionKey].Title;
        _logger.SettingsSectionReset(title, string.Join(", ", plan.Restored));
        _notifier.Notify(plan.Describe(title));
    }

    private bool CanResetSection(string? sectionKey)
    {
        return sectionKey is not null && ResetPlans.TryGetValue(sectionKey, out var plan) && plan.CanReset;
    }

    private void ResetField(SettingsResetField field)
    {
        field.Apply();
        NotifyDerivedOptions();
        RefreshResetAvailability();

        _logger.SettingsFieldReset(field.Label, field.DefaultText);
        _notifier.Notify($"«{field.Label}» вернулось к заводскому значению: {field.DefaultText}.");
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshResetAvailability();
    }

    private void NotifyDerivedOptions()
    {
        OnPropertyChanged(nameof(SelectedStartupOption));
        OnPropertyChanged(nameof(SelectedStartupAdminOption));
        OnPropertyChanged(nameof(SelectedDeleteModeOption));
        OnPropertyChanged(nameof(SelectedCompressionOption));
        OnPropertyChanged(nameof(SelectedGitFolderOption));
        NotifyAgentBackendChanged();
    }

    private void RefreshResetAvailability()
    {
        foreach (var field in ResetFields.Values)
        {
            field.RefreshAvailability();
        }
    }
}
