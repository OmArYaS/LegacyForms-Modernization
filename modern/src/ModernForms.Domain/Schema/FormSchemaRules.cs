using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace ModernForms.Domain.Schema;

/// <summary>
/// Form-schema rules. On the legacy side these checks never existed
/// server-side (the procedures only defended template existence); the modern
/// Application tier introduces them and keeps the stored-procedure workflow
/// semantics byte-for-byte (submit snapshot, ordered pending steps, 1..N).
/// </summary>
public static class FormSchemaRules
{
    private static readonly string[] KnownTypes = { "text", "multiline", "number", "date", "select" };

    private static readonly JsonSerializerOptions DefaultWrite = new();

    /// <summary>Validates a submitted payload against the schema; empty list means valid.</summary>
    public static IReadOnlyList<string> ValidatePayload(
        IReadOnlyList<FormFieldDefinition> fields, JsonObject? payload)
    {
        var failures = new List<string>();

        if (payload is null)
        {
            failures.Add("Payload is required.");
            return failures;
        }

        foreach (var field in fields)
        {
            payload.TryGetPropertyValue(field.Key, out var value);
            var text = StringOf(value);

            if (string.IsNullOrWhiteSpace(text))
            {
                if (field.Required)
                {
                    failures.Add($"'{field.Label}' is required.");
                }
                continue;
            }

            switch (field.Type)
            {
                case "number":
                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    {
                        failures.Add($"'{field.Label}' must be a number.");
                    }
                    else if (field.Min.HasValue && number < field.Min.Value)
                    {
                        failures.Add($"'{field.Label}' must be at least {field.Min.Value}.");
                    }
                    else if (field.Max.HasValue && number > field.Max.Value)
                    {
                        failures.Add($"'{field.Label}' must be at most {field.Max.Value}.");
                    }
                    break;

                case "date":
                    if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    {
                        failures.Add($"'{field.Label}' must be a date in yyyy-MM-dd form.");
                    }
                    break;

                case "select":
                    if (field.Options.Count > 0 && !field.Options.Contains(text))
                    {
                        failures.Add($"'{field.Label}' must be one of: {string.Join(", ", field.Options)}.");
                    }
                    break;

                case "text":
                case "multiline":
                    if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value)
                    {
                        failures.Add($"'{field.Label}' must be at most {field.MaxLength.Value} characters.");
                    }
                    break;

                default:
                    failures.Add($"Field '{field.Key}' declares unsupported type '{field.Type}'.");
                    break;
            }
        }

        // Unknown keys reject loudly — the payload is schema-bound, not free-form.
        foreach (var property in payload)
        {
            var known = fields.Any(f => string.Equals(f.Key, property.Key, StringComparison.Ordinal));
            if (!known)
            {
                failures.Add($"Unknown field '{property.Key}'.");
            }
        }

        return failures;
    }

    /// <summary>
    /// Workflow steps to snapshot into approval_step at submit time. Ordered 1..N,
    /// each gated on supervisor/admin — mirrors usp_SnapshotWorkflow behaviour.
    /// </summary>
    public static IReadOnlyList<(short StepSequence, string RoleRequired)> SnapshotSteps(
        IReadOnlyList<WorkflowStepDefinition> steps)
    {
        var ordered = steps.OrderBy(s => s.Sequence).ToList();
        var snapshot = new List<(short, string)>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var step = ordered[index];
            var expected = (short)(index + 1);

            if (step.Sequence != expected)
            {
                throw new SchemaRuleException(
                    $"Workflow steps must be ordered 1..N — found {step.Sequence} at position {expected}.");
            }

            if (step.Role is not ("supervisor" or "admin"))
            {
                throw new SchemaRuleException(
                    $"Workflow step {expected} requires role 'supervisor' or 'admin' — found '{step.Role}'.");
            }

            snapshot.Add((step.Sequence, step.Role));
        }

        if (snapshot.Count == 0)
        {
            throw new SchemaRuleException("Template has no workflow steps — submissions cannot route.");
        }

        return snapshot;
    }

    /// <summary>A stored schema must be renderable by both UIs and routable.</summary>
    public static IReadOnlyList<string> CheckSchema(string schemaJson)
    {
        var failures = new List<string>();
        FieldSchemaDocument document;

        try
        {
            document = SchemaParser.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            failures.Add($"Schema is not valid JSON: {ex.Message}");
            return failures;
        }

        foreach (var field in document.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                failures.Add("A field is missing its key.");
                continue;
            }

            if (!KnownTypes.Contains(field.Type))
            {
                failures.Add($"Field '{field.Key}' declares unsupported type '{field.Type}'.");
            }

            if (field.Type == "select" && field.Options.Count == 0)
            {
                failures.Add($"Select field '{field.Key}' declares no options.");
            }
        }

        try
        {
            SnapshotSteps(document.Workflow.Steps);
        }
        catch (SchemaRuleException ex)
        {
            failures.Add(ex.Message);
        }

        return failures;
    }

    /// <summary>Value text for validation: JSON scalars render unwrapped; complex nodes stringify.</summary>
    private static string? StringOf(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var text) => text,
            JsonValue v when v.TryGetValue<decimal>(out var n) =>
                n.ToString(CultureInfo.InvariantCulture) ?? v.ToJsonString(DefaultWrite),
            JsonValue v => v.ToJsonString(DefaultWrite),
            _ => node.ToJsonString(DefaultWrite) // arrays/objects stringify — type checks then fail loudly
        };
    }

    /// <summary>Raised when a schema document violates structural invariants.</summary>
    public sealed class SchemaRuleException : Exception
    {
        public SchemaRuleException(string message) : base(message) { }
    }
}
