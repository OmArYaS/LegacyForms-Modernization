namespace ModernForms.Domain.Entities;

/// <summary>
/// A reusable form definition. The field layout and the approval workflow both
/// live in <see cref="FieldSchemaJson"/> as one document — the legacy Knockout
/// renderer and the modern Angular renderer serialize the same source of truth.
/// </summary>
public sealed class FormTemplate
{
    public int Id { get; set; }

    /// <summary>Stable handle such as 'leave-request'.</summary>
    public string TemplateKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// JSON document: { fields:[…], workflow:{ steps:[…] } } — parsed into
    /// <see cref="Schema.FieldSchemaDocument"/> by the Application layer.
    /// </summary>
    public string FieldSchemaJson { get; set; } = "{}";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
