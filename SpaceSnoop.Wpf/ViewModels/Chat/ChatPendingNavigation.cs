using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public sealed record ChatPendingNavigation(string Key, string Page, PackIconLucideKind Icon)
{
    public string Text => $"{AgentPersona.Name} подготовил страницу «{Page}» – переход не сделан, чтобы не увести с ответа";

    public string ActionText => $"Открыть «{Page}»";

    public static ChatPendingNavigation? For(string sectionKey)
    {
        return sectionKey switch
        {
            SectionKey.Scan => new(sectionKey, "Сканирование", PackIconLucideKind.HardDrive),
            SectionKey.Sync => new(sectionKey, "Синхронизация", PackIconLucideKind.FolderSync),
            _ => null,
        };
    }
}
