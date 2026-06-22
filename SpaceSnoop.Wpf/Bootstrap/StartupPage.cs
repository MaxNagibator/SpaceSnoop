namespace SpaceSnoop.Wpf.Bootstrap;

/// <summary>Какую страницу показывать при запуске приложения.</summary>
public enum StartupPage
{
    /// <summary>Восстановить последнюю активную страницу прошлого сеанса.</summary>
    LastUsed = 0,

    /// <summary>Сканирование.</summary>
    Scan = 1,

    /// <summary>Синхронизация.</summary>
    Sync = 2,

    /// <summary>Логи.</summary>
    Logs = 3,
}
