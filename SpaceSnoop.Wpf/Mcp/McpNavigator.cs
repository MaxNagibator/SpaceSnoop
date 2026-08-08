namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpNavigator(IAppNavigator app)
{
    public event Action<string>? NavigationDeferred;

    public bool DeferNavigation { get; set; }

    public string? CurrentSectionKey => app.CurrentSectionKey;

    public bool DeferOrNavigate(string sectionKey)
    {
        if (DeferNavigation)
        {
            NavigationDeferred?.Invoke(sectionKey);
            return true;
        }

        app.TryNavigate(sectionKey);
        return false;
    }
}
