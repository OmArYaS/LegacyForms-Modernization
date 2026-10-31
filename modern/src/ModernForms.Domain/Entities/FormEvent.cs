namespace ModernForms.Domain.Entities;

/// <summary>
/// Append-only audit row written on every state change — the modern mirror of
/// the legacy form_event writes made inside the transition procedure.
/// </summary>
public sealed class FormEvent
{
    public long Id { get; set; }

    public long SubmissionId { get; set; }

    /// <summary>Navigational counterpart of <see cref="SubmissionId"/>.</summary>
    public FormSubmission? Submission { get; set; }

    /// <summary>created | submitted | step_approved | step_rejected | withdrawn.</summary>
    public string EventType { get; set; } = string.Empty;

    public long? ActorId { get; set; }

    public string? Detail { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}
