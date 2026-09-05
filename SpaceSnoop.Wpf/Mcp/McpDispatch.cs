using ModelContextProtocol;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Mcp;

internal static class McpDispatch
{
    public static void Run(Action action)
    {
        Run(() =>
        {
            action();
            return true;
        });
    }

    public static T Run<T>(Func<T> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        try
        {
            return dispatcher.Invoke(action,
                priority,
                CancellationToken.None,
                TimeSpan.FromSeconds(AppDefaults.McpDispatchTimeoutSeconds));
        }
        catch (TimeoutException)
        {
            throw new McpException("Окно приложения занято и не ответило вовремя – повторите позже.");
        }
    }
}
