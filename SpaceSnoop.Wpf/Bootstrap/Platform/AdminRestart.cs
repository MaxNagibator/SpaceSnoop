namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public static class AdminRestart
{
    public static bool Run(IApplicationLifetime lifetime)
    {
        if (!AdminElevation.TryRestartAsAdmin())
        {
            return false;
        }

        lifetime.Shutdown();
        return true;
    }
}
