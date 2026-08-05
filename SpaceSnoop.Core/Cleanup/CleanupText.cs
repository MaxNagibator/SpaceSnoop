namespace SpaceSnoop.Core.Cleanup;

public static class CleanupText
{
    public static string Availability(CleanupAvailability availability)
    {
        return availability switch
        {
            CleanupAvailability.Available => "доступна",
            CleanupAvailability.Missing => "каталога нет",
            CleanupAvailability.NeedsAdmin => "нужны права администратора",
            CleanupAvailability.Unsupported => "удаляется штатным средством Windows",
            CleanupAvailability.Unsafe => "путь ведёт в корень тома или наружу по ссылке – очистка запрещена",
            CleanupAvailability.Failed => "не удалось опросить",
            _ => "не замерена",
        };
    }
}
