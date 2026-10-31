using ModernForms.Domain.Enums;

namespace ModernForms.Domain.Entities;

/// <summary>
/// One filled form instance. <see cref="Version"/> is the optimistic
/// concurrency token checked against the caller's seen value exactly as the
/// legacy usp_Transition procedure did (FORMS:CONCURRENT_UPDATE on drift).
/// </summary>
public sealed class FormSubmission
{
    public long Id { get; set; }

    public int TemplateId { get; set; }

    /// <summary>Navigational counterpart of <see cref="TemplateId"/>.</summary>
    public FormTemplate? Template { get; set; }

    public long RequestorId { get; set; }

    /// <summary>Navigational counterpart of <see cref="RequestorId"/>.</summary>
    public Employee? Requestor { get; set; }

    /// <summary>State machine position — value-parity with the MySQL ENUM.</summary>
    public FormStatus Status { get; set; } = FormStatus.Draft;

    /// <summary>
    /// JSON object filled in against the template schema; validated by the
    /// Application layer before submit (required fields, types, bounds).
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>Populated when the submission reaches a terminal state.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Reviewer or withdrawal note (reject/withdraw only, per the legacy proc).</summary>
    public string? DecisionNote { get; set; }

    /// <summary>
    /// Optimistic-concurrency counter; every decision (and the final close)
    /// bumps it. configurations treat it as an EF concurrency token.
    /// </summary>
    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; }
}
