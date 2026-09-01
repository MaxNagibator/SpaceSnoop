namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed record SettingsResetPlan(
    string SectionKey,
    string Hint,
    IReadOnlyList<string> Restored,
    IReadOnlyList<string> Untouched,
    Action? Reset)
{
    public bool CanReset => Reset is not null;

    public string Describe(string sectionTitle)
    {
        var restored = $"«{sectionTitle}» вернулся к умолчанию: {string.Join(", ", Restored)}.";

        return Untouched.Count == 0
            ? restored
            : $"{restored} Не тронуты: {string.Join(", ", Untouched)}.";
    }
}
