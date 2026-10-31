using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernForms.Domain.Schema;

/// <summary>
/// Declarative definition of one form field, parsed from the template's
/// field_schema JSON document. Both renderers — the legacy Knockout viewmodel
/// and the modern Angular renderer — consume this same document, so the
/// contract lives exactly once (here, with the validators).
/// </summary>
public sealed class FormFieldDefinition
{
    /// <summary>Payload property name ('leaveType', 'amount', …).</summary>
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Type { get; init; } = "text";

    public bool Required { get; init; }

    /// <summary>Choice set for select fields.</summary>
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

    /// <summary>Lower bound for number fields.</summary>
    public decimal? Min { get; init; }

    /// <summary>Upper bound for number fields.</summary>
    public decimal? Max { get; init; }

    /// <summary>Maximum character length for text/multiline.</summary>
    public int? MaxLength { get; init; }
}

/// <summary>One approval stage copied into approval_step at submit time.</summary>
public sealed class WorkflowStepDefinition
{
    /// <summary>1-based stage order; the first pending step gates the workflow.</summary>
    public short Sequence { get; init; }

    /// <summary>'supervisor' or 'admin' — mirrors the legacy approval_step role ENUM.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "supervisor";
}

/// <summary>Root of the template field_schema document: { fields:[…], workflow:{ steps:[…] } }.</summary>
public sealed class FieldSchemaDocument
{
    public IReadOnlyList<FormFieldDefinition> Fields { get; init; } = Array.Empty<FormFieldDefinition>();

    /// <summary>JSON "workflow" section; empty when a template is not routable.</summary>
    [JsonPropertyName("workflow")]
    public WorkflowSection Workflow { get; init; } = new();

    /// <summary>Empty document — for templates without declarative schema (validation then fails loudly at submit).</summary>
    public static FieldSchemaDocument Empty() => new();
}

/// <summary>The optional "workflow" container in the schema document.</summary>
public sealed class WorkflowSection
{
    [JsonPropertyName("steps")]
    public IReadOnlyList<WorkflowStepDefinition> Steps { get; init; } = Array.Empty<WorkflowStepDefinition>();
}
