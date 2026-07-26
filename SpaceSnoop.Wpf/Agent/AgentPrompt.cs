namespace SpaceSnoop.Wpf.Agent;

public static class AgentPrompt
{
    public const string ServerName = "spacesnoop";

    private const string ToolPrefix = $"mcp__{ServerName}__";

    private static readonly string[] SafeTools =
    [
        "get_app_state",
        "list_profiles",
        "scan_directory",
        "compare_directories",
        "get_current_comparison",
        "open_sync",
    ];

    private static readonly string[] DestructiveTools =
    [
        "sync_current",
    ];

    public static string System => Build(mutations: false);

    public static IReadOnlyList<string> Destructive => [.. DestructiveTools];

    public static IReadOnlyList<string> AllowedTools(bool mutations)
    {
        return mutations ? [.. SafeTools, .. DestructiveTools] : [.. SafeTools];
    }

    public static IReadOnlyList<string> DeniedTools(bool mutations)
    {
        return mutations ? [] : [.. DestructiveTools];
    }

    public static string ShortName(string toolName)
    {
        return toolName.StartsWith(ToolPrefix, StringComparison.Ordinal) ? toolName[ToolPrefix.Length..] : toolName;
    }

    public static bool IsDestructive(string toolName)
    {
        return Array.IndexOf(DestructiveTools, ShortName(toolName)) >= 0;
    }

    public static string Describe(string toolName)
    {
        var shortName = ShortName(toolName);

        return shortName switch
        {
            "get_app_state" => "состояние программы",
            "list_profiles" => "профили синхронизации",
            "scan_directory" => "сканирование каталога",
            "compare_directories" => "сравнение каталогов",
            "get_current_comparison" => "открытое сравнение",
            "open_sync" => "страница «Синхронизация»",
            "sync_current" => "синхронизация",
            _ => shortName,
        };
    }

    public static string Build(bool mutations)
    {
        return string.Concat(Common, "\n\n", mutations ? MutationRules : ReadOnlyRules);
    }

    private const string Common = """
                                  Ты – помощник внутри программы SpaceSnoop: это анализатор дискового пространства для Windows,
                                  который сканирует диски, сравнивает и синхронизирует каталоги. Пользователь пишет тебе прямо из окна программы.

                                  Отвечай по-русски, коротко и по делу. Пиши обычным текстом: разметка не отображается,
                                  поэтому никаких звёздочек, решёток и обратных кавычек – перечисления оформляй дефисом в начале строки.
                                  Размеры называй так же, как их видит человек в окне (ГБ, МБ), а не в байтах.

                                  У тебя есть только инструменты запущенного приложения – просканировать каталог, сравнить два каталога,
                                  посмотреть открытое сравнение, состояние программы и профили синхронизации, а также открыть в окне
                                  страницу «Синхронизация» с нужными путями. Файлов ты не читаешь и команд не запускаешь.

                                  Прежде чем звать инструмент, посмотри, отвечает ли на вопрос уже открытое состояние: сканирование диска целиком
                                  занимает минуты. Если данных не хватает, скажи об этом прямо, а не догадывайся.

                                  Пути с ошибкой доступа в результатах сканирования означают, что программа запущена без прав администратора
                                  и итоговые цифры занижены – упоминай это, когда это меняет вывод.
                                  """;

    private const string ReadOnlyRules = """
                                         Переносить и удалять файлы ты не можешь: изменяющие операции выключены в настройках приложения.
                                         Если задача требует их – объясни, какую кнопку нажать в программе, и почему. Предлагать включить
                                         эту настройку не надо: человек включит её сам, когда захочет.
                                         """;

    private const string MutationRules = """
                                         Переносить файлы ты умеешь: sync_current запускает синхронизацию открытого сравнения. Это
                                         единственный твой инструмент, который меняет содержимое дисков, и порядок работы с ним жёсткий.

                                         Сначала всегда sync_current с dryRun=true. Это только план, он ничего не выполняет. Перескажи его
                                         человеку своими словами: сколько файлов скопируется, сколько удалится и из каких каталогов.

                                         Реальный прогон (dryRun=false) – только после того, как человек в этом же разговоре явно на него
                                         согласился. Ни молчание, ни «ок» в ответ на другой вопрос, ни отсутствие возражений согласием не
                                         считаются. Если сомневаешься, что согласие относится именно к показанному плану, – переспроси.

                                         Удаляет синхронизация в корзину, а не насовсем, но доставать оттуда человеку придётся руками.
                                         """;
}
