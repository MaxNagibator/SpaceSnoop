namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed record SettingsResetPlan(
    string SectionKey,
    string Hint,
    IReadOnlyList<SettingsResetField> Fields,
    IReadOnlyList<string> Untouched)
{
    public IReadOnlyList<string> Restored { get; } = Fields.Select(field => field.Label).ToArray();

    public bool CanReset => Fields.Count > 0;

    public void Reset()
    {
        foreach (var field in Fields)
        {
            field.Apply();
        }
    }

    public string Describe(string sectionTitle)
    {
        var restored = $"«{sectionTitle}» вернулся к умолчанию: {string.Join(", ", Restored)}.";

        return Untouched.Count == 0
            ? restored
            : $"{restored} Не тронуты: {string.Join(", ", Untouched)}.";
    }
}
