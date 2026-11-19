using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class StartupFailureNote
{
    public static string Describe(Exception exception, string? logsDirectory)
    {
        var roots = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions.Select(static inner => inner.GetBaseException())
            : [exception.GetBaseException()];

        var cause = roots.Any(static root => root is IOException or UnauthorizedAccessException)
            ? "Похоже, программе не удалось открыть свои файлы: их держит другая программа или не хватает прав на папку."
            : "Что-то пошло не так при запуске.";

        var report = logsDirectory is null
            ? "Журнал ошибок записать не удалось. Если ошибка повторится, сообщите разработчику, что программа не запускается."
            : $"Если ошибка повторится, отправьте разработчику журнал из папки:{Environment.NewLine}{logsDirectory}";

        return $"{AppInfo.Name} не удалось запустить. {cause}"
            + $"{Environment.NewLine}{Environment.NewLine}Попробуйте закрыть программу и открыть её снова. {report}";
    }
}
