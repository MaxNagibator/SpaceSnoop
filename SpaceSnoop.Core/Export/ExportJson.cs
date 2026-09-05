using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Core.Export;

/// <summary>
/// Общие настройки сериализации для машиночитаемых выгрузок: camelCase, enum'ы строками,
/// нестрогое экранирование (кириллица и разделители пути читаемы глазом).
/// </summary>
internal static class ExportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
