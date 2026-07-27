using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ChatHistoryStore
{
    public const string FileName = "chat-history.json";

    public const int TitleLength = 48;

    public const string UntitledConversation = "Новый разговор";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly ILogger<ChatHistoryStore> _logger;

    public ChatHistoryStore(ILogger<ChatHistoryStore> logger, string? filePath = null)
    {
        _logger = logger;
        FilePath = filePath ?? Path.Combine(AppStorage.DataDirectory, FileName);
    }

    public string FilePath { get; }

    public static string MakeTitle(string prompt)
    {
        var line = prompt.AsSpan().Trim();
        var end = line.IndexOfAny('\r', '\n');

        if (end >= 0)
        {
            line = line[..end].Trim();
        }

        if (line.IsEmpty)
        {
            return UntitledConversation;
        }

        if (line.Length <= TitleLength)
        {
            return line.ToString();
        }

        var head = line[..TitleLength];
        var space = head.LastIndexOf(' ');

        if (space > TitleLength / 2)
        {
            head = head[..space];
        }

        return string.Concat(head.TrimEnd(), "…");
    }

    // TODO: история режется по числу разговоров (AppDefaults.AgentHistoryLimit), длина отдельного разговора
    // не ограничена – файл растёт вместе с самым долгим. Резать по объёму, когда чтение истории начнёт
    // задерживать открытие страницы чата.
    public static List<ChatConversationRecord> Trim(IEnumerable<ChatConversationRecord> conversations)
    {
        return [.. conversations.Where(conversation => conversation.Messages.Count > 0).Take(AppDefaults.AgentHistoryLimit)];
    }

    public List<ChatConversationRecord> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<ChatHistoryDocument>(File.ReadAllText(FilePath), Options);

            return document is null ? [] : Trim(document.Conversations);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.ChatHistoryReadFailed(exception, FilePath);
            return [];
        }
    }

    public void Save(IEnumerable<ChatConversationRecord> conversations)
    {
        var document = new ChatHistoryDocument { Conversations = Trim(conversations) };

        try
        {
            if (document.Conversations.Count == 0 && !File.Exists(FilePath))
            {
                return;
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(document, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.ChatHistoryWriteFailed(exception, FilePath);
        }
    }
}
