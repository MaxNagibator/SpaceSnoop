namespace SpaceSnoop.Wpf.Agent;

public static class AgentPersona
{
    public const string Name = "Шнырь";

    public const string NameGenitive = "Шныря";

    public const string AskHeader = $"Спросить {NameGenitive}";

    public const string Greeting = "Хожу по вашим дискам и говорю, куда делось место и почему папки опять разошлись.";

    public const string InputPlaceholder = "Спросите Шныря – про диск, папку или несходящуюся синхронизацию";

    public const string Thinking = "Шнырь принюхивается…";

    public const string ReadOnlyNote = "Ничего не удаляет и не переносит сам – только смотрит.";

    public const string MutationsNote = "Синхронизацию, упаковку в архив и пометку на удаление может запустить сам, но сначала покажет план и дождётся вашего «да». Очистку системных корзин – только с подтверждением в окне: она удаляет мимо корзины.";

    public static string WorkingOn(string tool)
    {
        return $"Шнырь смотрит: {tool}";
    }
}
