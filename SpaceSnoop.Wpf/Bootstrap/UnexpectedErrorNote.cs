namespace SpaceSnoop.Wpf.Bootstrap;

public static class UnexpectedErrorNote
{
    public static string Describe(string logsDirectory)
    {
        return "Произошла непредвиденная ошибка. Последнее действие могло не завершиться – проверьте его результат и при необходимости повторите."
            + $"{Environment.NewLine}{Environment.NewLine}Если ошибка повторяется, отправьте разработчику журнал из папки:"
            + $"{Environment.NewLine}{logsDirectory}";
    }
}
