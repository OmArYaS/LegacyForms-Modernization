using ModernForms.Application.Common;
using ModernForms.Application.Contracts;
using ModernForms.Domain.Entities;
using ModernForms.Domain.Enums;
using ModernForms.Domain.Schema;
using Microsoft.EntityFrameworkCore;

namespace ModernForms.Application.Forms;

/// <summary>
/// The workflow engine. Phase 3 of the strangler migration: the rules that
/// lived in usp_Transition / usp_CreateSubmission for fifteen years now run
/// as tested C# — with one twist: application-level validation (payload &
/// schema checks) removes a class of defects the legacy code accepted.
/// Concurrency remains release-safe: decisions assert the caller's seen
/// Version and out fail loudly under contention.
/// </summary>
public interface IFormService
{
    /// <summary>Paged list; employees see only their own rows (role enforced here).</summary>
    Task<PagedResult<SubmissionListItemDto>> ListSubmissionsAsync(
        ICurrentUser caller, string? status, bool? mine, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Header + steps + events, with read access enforcement.</summary>
    Task<SubmissionDetailDto> GetSubmissionAsync(long submissionId, ICurrentUser caller, CancellationToken ct = default);

    /// <summary>Validates the payload against the template and (optionally) submits.</summary>
    Task<CreatedSubmissionDto> CreateAsync(CreateSubmissionRequest request, ICurrentUser caller, CancellationToken ct = default);

    /// <summary>Approve / reject / withdraw — same decision matrix as the legacy proc.</summary>
    Task<DecisionResultDto> DecideAsync(long submissionId, DecideRequest request, ICurrentUser caller, CancellationToken ct = default);

    /// <summary>Queue counters grouped per status and role gate.</summary>
    Task<IReadOnlyList<WorkloadRowDto>> WorkloadAsync(CancellationToken ct = default);
}

public sealed class FormService(
    IFormsDbContext db,
    IDateTime clock) : IFormService
{
    // ── Reads ──────────────────────────────────────────────────────────────

    public async Task<PagedResult<SubmissionListItemDto>> ListSubmissionsAsync(
        ICurrentUser caller, string? status, bool? mine, int page, int pageSize, CancellationToken ct = default)
    {
        page = Paging.NormalizePage(page);
        pageSize = Paging.NormalizePageSize(pageSize);

        var employee = await RequireEmployeeAsync(caller, ct);

        var roleIsEmployee = employee.Role == EmployeeRole.Employee;

        var statusFilter = ParseStatus(status); // null → open all; rule violation throws

        var query =
            from s in db.Submissions.AsNoTracking()
            join t in db.Templates.AsNoTracking() on s.TemplateId equals t.Id
            join e in db.Employees.AsNoTracking() on s.RequestorId equals e.Id
            where statusFilter == null || s.Status == statusFilter
            // Employees never see anyone else's rows — the legacy proc rule.
            where !roleIsEmployee || s.RequestorId == employee.Id
            where mine != true || s.RequestorId == employee.Id
            orderby s.SubmittedAtUtc descending, s.Id descending
            select new { s.Id, TemplateKey = t.TemplateKey, TemplateTitle = t.Title,
                s.RequestorId, RequestorName = e.FullName,
                s.Status, s.SubmittedAtUtc, s.ClosedAtUtc, s.DecisionNote, s.Version };

        var total = await query.CountAsync(ct);
        var raw = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = raw.Select(r => new SubmissionListItemDto(
            r.Id, r.TemplateKey, r.TemplateTitle,
            r.RequestorId, r.RequestorName,
            StatusText(r.Status),
            r.SubmittedAtUtc, r.ClosedAtUtc, r.DecisionNote, r.Version)).ToList();

        return new PagedResult<SubmissionListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<SubmissionDetailDto> GetSubmissionAsync(long submissionId, ICurrentUser caller, CancellationToken ct = default)
    {
        var employee = await RequireEmployeeAsync(caller, ct);

        var submission = await LoadSubmissionAsync(submissionId, ct);

        // usp_GetSubmission's ownership gate: employees read only their own.
        if (employee.Role == EmployeeRole.Employee && submission.RequestorId != employee.Id)
        {
            throw new BusinessRuleException("NOT_SUBMISSION_OWNER",
                "Retail callers may only read their own submissions.", 403);
        }

        var steps = await db.Steps.AsNoTracking()
            .Where(st => st.SubmissionId == submissionId)
            .OrderBy(st => st.StepSequence)
            .Select(st => new StepDto(
                st.Id, st.StepSequence,
                st.RoleRequired.ToString().ToLowerInvariant(),
                st.Decision.ToString().ToLowerInvariant(),
                st.DecidedById,
                st.DecidedBy != null ? st.DecidedBy.FullName : null,
                st.DecidedAtUtc, st.Note))
            .ToListAsync(ct);

        var events = await db.Events.AsNoTracking()
            .Where(ev => ev.SubmissionId == submissionId)
            .OrderBy(ev => ev.OccurredAtUtc).ThenBy(ev => ev.Id)
            .Select(ev => new EventDto(
                ev.EventType, ev.ActorId, ev.Detail, ev.OccurredAtUtc))
            .ToListAsync(ct);

        var template = submission.Template ?? await db.Templates.AsNoTracking().FirstAsync(t => t.Id == submission.TemplateId, ct);
        var requestorName = submission.Requestor != null ? submission.Requestor.FullName : "(deleted)";

        return new SubmissionDetailDto(
            submission.Id, template.TemplateKey, template.Title, template.FieldSchemaJson,
            submission.RequestorId, requestorName,
            StatusText(submission.Status),
            submission.PayloadJson,
            submission.SubmittedAtUtc, submission.ClosedAtUtc, submission.DecisionNote, submission.Version,
            steps, events);
    }

    // ── Writes ─────────────────────────────────────────────────────────────

    public async Task<CreatedSubmissionDto> CreateAsync(
        CreateSubmissionRequest request, ICurrentUser caller, CancellationToken ct = default)
    {
        var employee = await RequireEmployeeAsync(caller, ct);

        var template = await db.Templates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TemplateKey == request.TemplateKey && t.IsActive, ct)
            ?? throw new BusinessRuleException("TEMPLATE_NOT_FOUND",
                $"No active template keyed '{request.TemplateKey}'.", 404);

        var payloadNode = SchemaParser.ReadPayload(request.PayloadJson ?? "{}");
        var schema = SchemaParser.Parse(template.FieldSchemaJson);
        var failures = FormSchemaRules.ValidatePayload(schema.Fields, payloadNode);

        if (failures.Count > 0)
        {
            throw new BusinessRuleException("INVALID_PAYLOAD",
                "Payload misses the template schema: " + string.Join(' ', failures), 400);
        }

        var submission = new FormSubmission
        {
            TemplateId = template.Id,
            RequestorId = employee.Id,
            Status = request.SubmitNow ? FormStatus.Submitted : FormStatus.Draft,
            PayloadJson = payloadNode!.ToJsonString(),
            SubmittedAtUtc = request.SubmitNow ? clock.UtcNow : null,
            Version = 1,
            CreatedAtUtc = clock.UtcNow
        };

        await using var scope = await db.BeginTransactionAsync(ct);
        await db.Submissions.AddAsync(submission, ct);
        await db.SaveChangesAsync(ct);

        await db.Events.AddAsync(new FormEvent
        {
            SubmissionId = submission.Id,
            EventType = "created",
            ActorId = employee.Id,
            Detail = request.SubmitNow ? "submitted" : "draft",
            OccurredAtUtc = clock.UtcNow
        }, ct);

        if (request.SubmitNow)
        {
            await SnapshotWorkflowAsync(submission, schema.Workflow.Steps, ct);
            await db.Events.AddAsync(new FormEvent
            {
                SubmissionId = submission.Id,
                EventType = "submitted",
                ActorId = employee.Id,
                Detail = null,
                OccurredAtUtc = clock.UtcNow
            }, ct);
        }

        await db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);

        return new CreatedSubmissionDto(
            submission.Id, template.TemplateKey,
            StatusText(submission.Status),
            submission.SubmittedAtUtc, submission.Version);
    }

    public async Task<DecisionResultDto> DecideAsync(
        long submissionId, DecideRequest request, ICurrentUser caller, CancellationToken ct = default)
    {
        var employee = await RequireEmployeeAsync(caller, ct);

        var decisionText = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
        var isWithdraw = decisionText == "withdrawn";
        StepDecision? decision;

        if (isWithdraw)
        {
            decision = null; // withdrawn has no step decision — it is a requestor action
        }
        else if (decisionText == "approved" || decisionText == "rejected")
        {
            decision = decisionText == "approved" ? StepDecision.Approved : StepDecision.Rejected;
        }
        else
        {
            throw new BusinessRuleException("INVALID_DECISION",
                $"Decision must be approved, rejected or withdrawn — found '{request.Decision}'.", 400);
        }

        await using var scope = await db.BeginTransactionAsync(ct);

        // Snapshot semantics: load within the transaction, assert the seen
        // version, mutate, commit. EF concurrency token guards inter-leg races.
        var submission = await db.Submissions
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new BusinessRuleException("SUBMISSION_NOT_FOUND",
                $"Submission {submissionId} does not exist.", 404);

        if (submission.Version != request.SeenVersion)
        {
            throw new BusinessRuleException("CONCURRENT_UPDATE",
                "Submission changed since you loaded it — reload and retry.", 409);
        }

        if (submission.Status is not (FormStatus.Submitted or FormStatus.InReview))
        {
            throw new BusinessRuleException("TERMINAL_STATE",
                $"Submission is already closed ({submission.Status}).", 409);
        }

        int newVersion;

        if (isWithdraw)
        {
            newVersion = await WithdrawAsync(submission, employee, request.Note, ct);
        }
        else
        {
            // decision is never null on this branch (only withdraw sets null above)
            newVersion = await RecordStepDecisionAsync(submission, employee, decision!.Value, request.Note, ct);
        }

        await db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);

        return new DecisionResultDto(newVersion, StatusText(submission.Status));
    }

    public async Task<IReadOnlyList<WorkloadRowDto>> WorkloadAsync(CancellationToken ct = default)
    {
        // Mirror of vw_workload: open submissions joined to their pending steps,
        // counted per status and role gate. (Grouping happens client-side —
        // ToString() inside a SQL GroupBy is untranslatable on any provider.)
        var open = await (
            from s in db.Submissions.AsNoTracking()
            join st in db.Steps.AsNoTracking() on s.Id equals st.SubmissionId
            where (s.Status == FormStatus.Submitted || s.Status == FormStatus.InReview)
                  && st.Decision == StepDecision.Pending
            select new { Status = s.Status, Gate = st.RoleRequired })
            .ToListAsync(ct);

        // Gate priority mirrors the dashboard: supervisor queue first, then admin.
        return open
            .GroupBy(r => new { Status = r.Status.ToString().ToLowerInvariant(), Role = r.Gate.ToString().ToLowerInvariant() })
            .Select(g => new WorkloadRowDto(g.Key.Status, g.Key.Role, g.Count()))
            .OrderBy(r => r.RoleRequired == "supervisor" ? 0 : 1)
            .ThenBy(r => r.Status)
            .ToList();
    }

    // ── Rule internals (the mirrors of usp_Transition branches) ───────────

    private async Task<int> WithdrawAsync(FormSubmission submission, Employee actor, string? note, CancellationToken ct)
    {
        if (submission.RequestorId != actor.Id)
        {
            throw new BusinessRuleException("NOT_SUBMISSION_OWNER",
                "Only the requestor can withdraw a submission.", 403);
        }

        var decided = await db.Steps.AsNoTracking()
            .AnyAsync(st => st.SubmissionId == submission.Id && st.DecidedAtUtc != null, ct);

        if (decided)
        {
            throw new BusinessRuleException("WITHDRAW_TOO_LATE",
                "A reviewer already recorded a decision — the request can no longer be withdrawn.", 409);
        }

        submission.Status = FormStatus.Withdrawn;
        submission.ClosedAtUtc = clock.UtcNow;
        submission.DecisionNote = note ?? string.Empty;
        submission.Version += 1;

        await db.Events.AddAsync(new FormEvent
        {
            SubmissionId = submission.Id,
            EventType = "withdrawn",
            ActorId = actor.Id,
            Detail = note,
            OccurredAtUtc = clock.UtcNow
        }, ct);

        return submission.Version;
    }

    private async Task<int> RecordStepDecisionAsync(
        FormSubmission submission, Employee actor, StepDecision decision, string? note, CancellationToken ct)
    {
        var firstPending = await db.Steps
            .Where(st => st.SubmissionId == submission.Id && st.Decision == StepDecision.Pending)
            .OrderBy(st => st.StepSequence)
            .FirstOrDefaultAsync(ct);

        if (firstPending is null)
        {
            throw new BusinessRuleException("NO_PENDING_STEP",
                "No pending approval step remains — the submission state is inconsistent.", 409);
        }

        // Role gate: admins may discharge any step (same as the legacy proc).
        var roleRequired = firstPending.RoleRequired;
        if (actor.Role == EmployeeRole.Employee ||
            (actor.Role == EmployeeRole.Supervisor && roleRequired == ApprovalRole.Admin))
        {
            throw new BusinessRuleException("ROLE_MISMATCH",
                $"Step {firstPending.StepSequence} requires the {roleRequired.ToString().ToLowerInvariant()} role — caller is {actor.Role.ToString().ToLowerInvariant()}.", 403);
        }

        firstPending.Decision = decision;
        firstPending.DecidedById = actor.Id;
        firstPending.DecidedAtUtc = clock.UtcNow;
        firstPending.Note = note;

        if (decision == StepDecision.Rejected)
        {
            submission.Status = FormStatus.Rejected;
            submission.ClosedAtUtc = clock.UtcNow;
            submission.DecisionNote = note;
        }
        else
        {
            var remaining = await db.Steps.AsNoTracking()
                .CountAsync(st => st.SubmissionId == submission.Id
                                  && st.Id != firstPending.Id
                                  && st.Decision == StepDecision.Pending, ct);

            if (remaining == 0)
            {
                submission.Status = FormStatus.Approved;
                submission.ClosedAtUtc = clock.UtcNow;
            }
            else
            {
                submission.Status = FormStatus.InReview;
            }
        }

        submission.Version += 1;

        await db.Events.AddAsync(new FormEvent
        {
            SubmissionId = submission.Id,
            EventType = $"step_{decision.ToString().ToLowerInvariant()}",
            ActorId = actor.Id,
            Detail = $"step {firstPending.StepSequence}",
            OccurredAtUtc = clock.UtcNow
        }, ct);

        return submission.Version;
    }

    private async Task SnapshotWorkflowAsync(FormSubmission submission, IReadOnlyList<WorkflowStepDefinition> steps, CancellationToken ct)
    {
        var snapshot = FormSchemaRules.SnapshotSteps(steps);

        foreach (var (sequence, role) in snapshot)
        {
            await db.Steps.AddAsync(new ApprovalStep
            {
                SubmissionId = submission.Id,
                StepSequence = sequence,
                RoleRequired = role == "admin" ? ApprovalRole.Admin : ApprovalRole.Supervisor,
                Decision = StepDecision.Pending
            }, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Shared helpers ─────────────────────────────────────────────────────

    private async Task<Employee> RequireEmployeeAsync(ICurrentUser caller, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeKey == caller.EmployeeKey, ct);

        if (employee is null || !employee.Active)
        {
            throw new BusinessRuleException("REQUESTOR_INACTIVE", "Your employee record is inactive or unknown.", 403);
        }

        return employee;
    }

    /// <summary>Canonical status vocabulary — matches the MySQL ENUM members.</summary>
    internal static string StatusText(FormStatus status) => status switch
    {
        FormStatus.Draft => "draft",
        FormStatus.Submitted => "submitted",
        FormStatus.InReview => "in_review",
        FormStatus.Approved => "approved",
        FormStatus.Rejected => "rejected",
        FormStatus.Withdrawn => "withdrawn",
        _ => status.ToString().ToLowerInvariant()
    };

    private static FormStatus? ParseStatus(string? status)
    {        if (string.IsNullOrWhiteSpace(status)) return null;

        return status.Trim().ToLowerInvariant() switch
        {
            "draft" => FormStatus.Draft,
            "submitted" => FormStatus.Submitted,
            "in_review" => FormStatus.InReview,
            "approved" => FormStatus.Approved,
            "rejected" => FormStatus.Rejected,
            "withdrawn" => FormStatus.Withdrawn,
            _ => throw new BusinessRuleException("INVALID_STATUS",
                $"Unknown status filter '{status}'.", 400)
        };
    }

    private async Task<FormSubmission> LoadSubmissionAsync(long submissionId, CancellationToken ct)
    {
        var submission = await db.Submissions.AsNoTracking()
            .Include(s => s.Template)
            .Include(s => s.Requestor)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);

        if (submission is null)
        {
            throw new NotFoundException($"Submission {submissionId} does not exist.");
        }

        return submission;
    }
}
