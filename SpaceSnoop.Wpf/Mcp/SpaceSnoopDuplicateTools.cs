using ModelContextProtocol.Server;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.Mcp;

[McpServerToolType]
public sealed class SpaceSnoopDuplicateTools
{
    private SpaceSnoopDuplicateTools()
    {
    }

    [McpServerTool(Name = "find_duplicates")]
    [Description("Ищет дубликаты в дереве, которое сейчас открыто на странице «Сканирование»: файлы одного размера сличаются побайтно, совпадение хеша дубликатом не считается. Ничего не удаляет и не помечает. Жёсткие и символьные ссылки показываются членом группы, но места не возвращают, поэтому reclaimableBytes считает только настоящие копии. Скана нет – ошибка: сначала scan_directory с show=true.")]
    public static Task<string> FindDuplicatesAsync(
        McpBridge bridge,
        [Description("Файлы меньше этого размера в байтах не рассматриваются")] long minSize = 1,
        [Description("Сколько групп и сколько файлов в группе выгружать (крупнейшие по возвращаемому месту)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.Scan.FindDuplicatesAsync(minSize, entryLimit, cancellationToken);
    }
}
