namespace ModernForms.Domain.Enums;

/// <summary>
/// Submission state machine: draft → submitted → in_review → approved / rejected,
/// with withdrawn reachable from submitted/in_review before any decision.
/// Values must stay identical to the MySQL ENUM column in form_submission —
/// the legacy schema does not get rewritten under this programme.
/// </summary>
public enum FormStatus
{
    Draft = 0,
    Submitted = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4,
    Withdrawn = 5
}

/// <summary>Role gate of a workflow step; mirrors approval_step.role_required.</summary>
public enum ApprovalRole
{
    Supervisor = 1,
    Admin = 2
}

/// <summary>Step decision; mirrors approval_step.decision.</summary>
public enum StepDecision
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>Caller clearance resolved from the JWT role claim.</summary>
public enum EmployeeRole
{
    Employee = 1,
    Supervisor = 2,
    Admin = 3
}
