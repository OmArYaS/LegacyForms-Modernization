using ModernForms.Domain.Enums;

namespace ModernForms.Domain.Entities;

/// <summary>
/// An approval stage snapshotted from the template workflow at submit time so
/// that later template edits never reshape in-flight requests (the legacy
/// usp_SnapshotWorkflow behaviour, now expressed as an owned entity graph).
/// </summary>
public sealed class ApprovalStep
{
    public long Id { get; set; }

    public long SubmissionId { get; set; }

    /// <summary>Navigational counterpart of <see cref="SubmissionId"/>.</summary>
    public FormSubmission? Submission { get; set; }

    /// <summary>1-based stage order; the first pending step gates the workflow.</summary>
    public short StepSequence { get; set; }

    public ApprovalRole RoleRequired { get; set; } = ApprovalRole.Supervisor;

    public StepDecision Decision { get; set; } = StepDecision.Pending;

    public long? DecidedById { get; set; }

    public Employee? DecidedBy { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    public string? Note { get; set; }
}
