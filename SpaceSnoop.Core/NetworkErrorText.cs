using System.ComponentModel;

namespace SpaceSnoop.Core;

public static class NetworkErrorText
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorBadNetPath = 53;
    private const int ErrorBadNetName = 67;
    private const int ErrorInvalidPassword = 86;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorLogonFailure = 1326;
    private const int FacilityWin32Mask = unchecked((int)0xFFFF_0000);
    private const int FacilityWin32 = unchecked((int)0x8007_0000);

    public static string Describe(Exception exception, string path, bool isElevated)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var target = string.IsNullOrWhiteSpace(path) ? "сетевой путь" : path.Trim();
        var host = UncPath.GetHost(target);

        return ExtractWin32ErrorCode(exception) switch
        {
            ErrorAccessDenied => DescribeAccessDenied(target, isElevated),
            ErrorInvalidPassword or ErrorLogonFailure => DescribeLogonFailure(target, isElevated),
            ErrorSessionCredentialConflict => DescribeCredentialConflict(target, host),
            ErrorBadNetPath => DescribeBadNetPath(target, host),
            ErrorBadNetName => DescribeBadNetName(target),
            _ => exception.Message,
        };
    }

    internal static int? ExtractWin32ErrorCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win32 && win32.NativeErrorCode != 0)
            {
                return win32.NativeErrorCode;
            }

            if ((current.HResult & FacilityWin32Mask) == FacilityWin32)
            {
                var code = current.HResult & 0xFFFF;

                if (code != 0)
                {
                    return code;
                }
            }
        }

        return null;
    }

    private static string DescribeAccessDenied(string target, bool isElevated)
    {
        return isElevated
            ? $"Нет доступа к «{target}»: сеанс администратора не видит временный вход Проводника. Сохраните учётные данные для сервера в Диспетчере учётных данных Windows или запустите SpaceSnoop без прав администратора – чтение $MFT для сети всё равно не используется."
            : $"Нет доступа к «{target}». Проверьте права и учётные данные для сервера в Диспетчере учётных данных Windows.";
    }

    private static string DescribeLogonFailure(string target, bool isElevated)
    {
        return isElevated
            ? $"Не удалось войти на «{target}» (неверный логин или пароль, либо учётные данные недоступны в сеансе администратора). Сохраните учётные данные для сервера в Диспетчере учётных данных Windows или запустите SpaceSnoop без прав администратора – чтение $MFT для сети всё равно не используется."
            : $"Неверное имя пользователя или пароль для «{target}». Проверьте или обновите учётные данные в Диспетчере учётных данных Windows.";
    }

    private static string DescribeCredentialConflict(string target, string? host)
    {
        var server = string.IsNullOrEmpty(host) ? "этому серверу" : $"серверу «{host}»";
        return $"Конфликт учётных данных для «{target}»: Windows запрещает два одновременных подключения к {server} под разными логинами. Закройте прежнее подключение или обратитесь по IP-адресу.";
    }

    private static string DescribeBadNetPath(string target, string? host)
    {
        return string.IsNullOrEmpty(host)
            ? $"Сетевой путь «{target}» недоступен. Проверьте имя сервера и подключение к сети."
            : $"Сервер «{host}» или путь «{target}» недоступен. Проверьте имя сервера и подключение к сети.";
    }

    private static string DescribeBadNetName(string target)
    {
        return $"Сетевая папка «{target}» не найдена на сервере. Проверьте имя общей папки и права доступа.";
    }
}
