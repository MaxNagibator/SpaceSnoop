namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpNavigator
{
    private ShellViewModel? _shell;

    public event Action<string>? NavigationDeferred;

    public bool DeferNavigation { get; set; }

    public string? CurrentSectionKey => _shell?.CurrentSectionKey;

    public void Attach(ShellViewModel shell)
    {
        _shell = shell;
    }

    public bool DeferOrNavigate(string sectionKey)
    {
        if (DeferNavigation)
        {
            NavigationDeferred?.Invoke(sectionKey);
            return true;
        }

        _shell?.TryNavigate(sectionKey);
        return false;
    }
}
