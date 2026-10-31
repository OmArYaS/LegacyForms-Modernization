using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModernForms.Domain.Schema;

/// <summary>
/// Json source of truth ⇄ typed document for template field schemas.
/// Kept in the Domain layer so payload validation and workflow snapshot
/// logic are testable without any persistence stack.
/// </summary>
public static class SchemaParser
{
    private static readonly JsonSerializerOptions Options = new(
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>Parses the stored JSON document; empty input yields an empty document.</summary>
    public static FieldSchemaDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FieldSchemaDocument.Empty();
        }

        return JsonSerializer.Deserialize<FieldSchemaDocument>(json, Options)
               ?? FieldSchemaDocument.Empty();
    }

    /// <summary>Serializes a typed document back to the storage format.</summary>
    public static string Serialize(FieldSchemaDocument document) => JsonSerializer.Serialize(document, Options);

    /// <summary>Reads a payload object for validation; null when empty/absent.</summary>
    public static JsonObject? ReadPayload(string payloadJson)
    {
        return string.IsNullOrWhiteSpace(payloadJson)
            ? null
            : JsonNode.Parse(payloadJson) as JsonObject;
    }
}
