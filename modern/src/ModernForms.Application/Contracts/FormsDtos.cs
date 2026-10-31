namespace ModernForms.Application.Contracts;

/// <summary>Result columns of usp_GetActiveTemplates — Contract identical across eras.</summary>
public sealed record TemplateDto(
    int TemplateId,
    string TemplateKey,
    string Title,
    string? Description,
    string FieldSchema);

/// <summary>Result columns of usp_ListSubmissions.</summary>
public sealed record SubmissionListItemDto(
    long SubmissionId,
    string TemplateKey,
    string TemplateTitle,
    long RequestorId,
    string RequestorName,
    string Status,
    DateTime? SubmittedAt,
    DateTime? ClosedAt,
    string? DecisionNote,
    int Version);

/// <summary>Result columns set two of usp_GetSubmission (approval steps).</summary>
public sealed record StepDto(
    long StepId,
    int StepSequence,
    string RoleRequired,
    string Decision,
    long? DecidedBy,
    string? DecidedByName,
    DateTime? DecidedAt,
    string? Note);

/// <summary>Result set three of usp_GetSubmission (audit trail).</summary>
public sealed record EventDto(
    string EventType,
    long? ActorId,
    string? Detail,
    DateTime OccurredAt);

/// <summary>The detail aggregate the detail screens hydrate (header + steps + events).</summary>
public sealed record SubmissionDetailDto(
    long SubmissionId,
    string TemplateKey,
    string TemplateTitle,
    string TemplateFieldSchema,
    long RequestorId,
    string RequestorName,
    string Status,
    string Payload,
    DateTime? SubmittedAt,
    DateTime? ClosedAt,
    string? DecisionNote,
    int Version,
    IReadOnlyList<StepDto> Steps,
    IReadOnlyList<EventDto> Events);

/// <summary>POST /api/submissions body — payload validated against the template schema.</summary>
public sealed record CreateSubmissionRequest(
    string TemplateKey,
    string? PayloadJson,
    bool SubmitNow);

/// <summary>Outcome of a create accepted by the workflow engine.</summary>
public sealed record CreatedSubmissionDto(
    long SubmissionId,
    string TemplateKey,
    string Status,
    DateTime? SubmittedAt,
    int Version);

/// <summary>POST /api/submissions/{id}/decide body — mirrors usp_Transition inputs.</summary>
public sealed record DecideRequest(
    string Decision,
    string? Note,
    int SeenVersion);

/// <summary>Outcome of a decision accepted by the workflow engine (mirrors po_new_version).</summary>
public sealed record DecisionResultDto(
    int NewVersion,
    string Status);

/// <summary>Queue counters (usp_WorkloadSummary).</summary>
public sealed record WorkloadRowDto(
    string Status,
    string RoleRequired,
    int PendingSubmissions);
