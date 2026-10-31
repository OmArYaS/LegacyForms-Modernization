using Microsoft.EntityFrameworkCore;

using ModernForms.Application.Common;
using ModernForms.Application.Contracts;
using ModernForms.Application.Forms;
using ModernForms.Domain.Entities;
using ModernForms.Domain.Enums;
using ModernForms.Infrastructure.Persistence;
using ModernForms.Tests.TestSupport;

using Xunit;

namespace ModernForms.Tests.Application;

/// <summary>
/// The workflow engine under test mirrors every decision branch of the
/// legacy usp_Transition / usp_CreateSubmission procedures (see
/// database/mysql/02_stored_procedures.sql and the FORMS:* error codes):
/// terminal-state guard, pending-step walk, role gates, withdraw window,
/// optimistic concurrency and the version increment contract.
/// </summary>
public sealed class FormWorkflowTests : IAsyncLifetime
{
    private FormsTestDb host = null!;

    private FormsTestDb.SeedResult seed = null!;

    public async Task InitializeAsync()
    {
        host = await FormsTestDb.CreateAsync();
        seed = await host.SeedAsync(FixedClock.Default.UtcNow);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    private FormService NewService() => new(host.Db, FixedClock.Default);

    private async Task<FormSubmission> SeededSubmissionAsync(
        int templateId, long requestorId, bool submitNow = true, string schema = FormsTestDb.MinimalSchema)
    {
        var submission = new FormSubmission
        {
            TemplateId = templateId,
            RequestorId = requestorId,
            Status = submitNow ? FormStatus.Submitted : FormStatus.Draft,
            PayloadJson = """{"leaveType":"Annual","reason":"Contract test"}""",
            SubmittedAtUtc = submitNow ? FixedClock.Default.UtcNow : null,
            Version = 1,
            CreatedAtUtc = FixedClock.Default.UtcNow
        };

        host.Db.Submissions.Add(submission);
        await host.Db.SaveChangesAsync();

        var template = await host.Db.Templates.FirstAsync(t => t.Id == templateId);
        var parsed = ModernForms.Domain.Schema.SchemaParser.Parse(template.FieldSchemaJson);

        foreach (var step in parsed.Workflow.Steps.OrderBy(s => s.Sequence))
        {
            host.Db.Steps.Add(new ApprovalStep
            {
                SubmissionId = submission.Id,
                StepSequence = step.Sequence,
                RoleRequired = step.Role == "admin" ? ApprovalRole.Admin : ApprovalRole.Supervisor,
                Decision = StepDecision.Pending
            });
        }

        await host.Db.SaveChangesAsync();
        return submission;
    }

    // ── list + read access ───────────────────────────────────────────────────

    [Fact]
    public async Task Employee_listing_sees_only_own_rows()
    {
        await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);
        await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);

        var service = NewService();

        var employeePage = await service.ListSubmissionsAsync(TestUser.Employee("usr_amira"), null, null, 1, 25);
        var supervisorPage = await service.ListSubmissionsAsync(TestUser.Supervisor, null, null, 1, 25);

        Assert.Equal(1, employeePage.TotalCount);
        Assert.Equal(seed.AmiraId, employeePage.Items[0].RequestorId);
        Assert.Equal(2, supervisorPage.TotalCount);
    }

    [Fact]
    public async Task Employee_cannot_read_another_employees_submission()
    {
        var submission = await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().GetSubmissionAsync(submission.Id, TestUser.Employee("usr_amira")));

        Assert.Equal("NOT_SUBMISSION_OWNER", failure.Code);
    }

    [Fact]
    public async Task Unknown_status_filter_is_rejected()
    {
        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().ListSubmissionsAsync(TestUser.Supervisor, "maybe", null, 1, 25));

        Assert.Equal("INVALID_STATUS", failure.Code);
    }

    [Fact]
    public async Task Unknown_demo_user_is_rejected()
    {
        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().ListSubmissionsAsync(TestUser.Employee("usr_ghost"), null, null, 1, 25));

        Assert.Equal("REQUESTOR_INACTIVE", failure.Code);
    }

    // ── create + snapshot ────────────────────────────────────────────────────

    [Fact]
    public async Task Submitted_request_snapshots_the_workflow_steps()
    {
        var service = NewService();

        var created = await service.CreateAsync(
            new CreateSubmissionRequest(
                "leave-request",
                """{"leaveType":"Sick","reason":"Doctor visit"}""",
                SubmitNow: true),
            TestUser.Employee("usr_amira"));

        Assert.Equal("submitted", created.Status);
        Assert.Equal(1, created.Version);

        var steps = await host.Db.Steps.AsNoTracking()
            .Where(st => st.SubmissionId == created.SubmissionId)
            .OrderBy(st => st.StepSequence).ToListAsync();
        var events = await host.Db.Events.AsNoTracking()
            .Where(ev => ev.SubmissionId == created.SubmissionId)
            .Select(ev => ev.EventType).ToListAsync();

        var single = Assert.Single(steps);
        Assert.Equal(1, single.StepSequence);
        Assert.Equal(ApprovalRole.Supervisor, single.RoleRequired);
        Assert.Equal(StepDecision.Pending, single.Decision);
        Assert.Contains("created", events);
        Assert.Contains("submitted", events);
    }

    [Fact]
    public async Task Draft_request_skips_the_snapshot()
    {
        var service = NewService();

        var created = await service.CreateAsync(
            new CreateSubmissionRequest(
                "leave-request",
                """{"leaveType":"Sick","reason":"Consultation on Monday"}""",
                SubmitNow: false),
            TestUser.Employee("usr_amira"));

        Assert.Equal("draft", created.Status);
        Assert.Null(created.SubmittedAt);
        Assert.False(await host.Db.Steps.AnyAsync(st => st.SubmissionId == created.SubmissionId));
    }

    [Fact]
    public async Task Inactive_template_is_not_creatable()
    {
        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().CreateAsync(
                new CreateSubmissionRequest("old-form", """{"leaveType":"Annual","reason":"retired"}""", SubmitNow: true),
                TestUser.Employee("usr_amira")));

        Assert.Equal("TEMPLATE_NOT_FOUND", failure.Code);
    }

    [Fact]
    public async Task Payload_violations_create_nothing()
    {
        var service = NewService();

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync(
                new CreateSubmissionRequest("leave-request", """{"leaveType":"Golf"}""", SubmitNow: true),
                TestUser.Employee("usr_amira")));

        Assert.Equal("INVALID_PAYLOAD", failure.Code);
        Assert.False(await host.Db.Submissions.AnyAsync());
    }

    // ── decision matrix (usp_Transition mirrors) ─────────────────────────────

    [Fact]
    public async Task Supervisor_decision_moves_status_in_review_until_last_step()
    {
        var submission = await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);
        var service = NewService();

        var result = await service.DecideAsync(
            submission.Id, new DecideRequest("approved", "Receipts checked.", 1), TestUser.Supervisor);

        Assert.Equal("in_review", result.Status);
        Assert.Equal(2, result.NewVersion);
        Assert.Equal("InReview", (await host.Db.Submissions.AsNoTracking().FirstAsync(s => s.Id == submission.Id)).Status.ToString());
    }

    [Fact]
    public async Task Final_admin_step_closes_the_submission_approved()
    {
        var submission = await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);
        var service = NewService();

        await service.DecideAsync(submission.Id, new DecideRequest("approved", "Supervisons ok.", 1), TestUser.Supervisor);
        var result = await service.DecideAsync(submission.Id, new DecideRequest("approved", "Finance ok.", 2), TestUser.Admin);

        Assert.Equal("approved", result.Status);
        Assert.Equal(3, result.NewVersion);
        Assert.NotNull((await host.Db.Submissions.AsNoTracking().FirstAsync(s => s.Id == submission.Id)).ClosedAtUtc);
    }

    [Fact]
    public async Task Rejection_closes_the_submission_with_the_note()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);
        var service = NewService();

        var result = await service.DecideAsync(
            submission.Id, new DecideRequest("rejected", "Peak season freeze.", 1), TestUser.Supervisor);

        Assert.Equal("rejected", result.Status);
        var row = await host.Db.Submissions.AsNoTracking().FirstAsync(s => s.Id == submission.Id);
        Assert.Equal("Peak season freeze.", row.DecisionNote);
        Assert.Equal(2, row.Version);
    }

    [Fact]
    public async Task Employee_role_cannot_decide()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().DecideAsync(
                submission.Id, new DecideRequest("approved", null, 1), TestUser.Employee("usr_amira")));

        Assert.Equal("ROLE_MISMATCH", failure.Code);
    }

    [Fact]
    public async Task Supervisor_cannot_discharge_the_admin_step()
    {
        // Step 1 (supervisor) is approved; the next pending step is admin-gated —
        // a supervisor arriving again must be refused by the role gate.
        var submission = await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);
        var service = NewService();
        await service.DecideAsync(submission.Id, new DecideRequest("approved", "Queue ok.", 1), TestUser.Supervisor);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.DecideAsync(
                submission.Id, new DecideRequest("approved", "Out of turn.", 2), TestUser.Supervisor));

        Assert.Equal("ROLE_MISMATCH", failure.Code);
    }

    [Fact]
    public async Task Admin_can_step_in_for_the_supervisor_gate()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);

        var result = await NewService().DecideAsync(
            submission.Id, new DecideRequest("approved", "Stepping in.", 1), TestUser.Admin);

        Assert.Equal("approved", result.Status);
    }

    [Fact]
    public async Task Seen_version_mismatch_is_a_conflict_not_a_decide()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().DecideAsync(
                submission.Id, new DecideRequest("approved", null, 99), TestUser.Supervisor));

        Assert.Equal("CONCURRENT_UPDATE", failure.Code);
    }

    [Fact]
    public async Task Closed_submission_is_terminal()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);
        var service = NewService();
        await service.DecideAsync(submission.Id, new DecideRequest("approved", null, 1), TestUser.Supervisor);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.DecideAsync(
                submission.Id, new DecideRequest("approved", null, 2), TestUser.Admin));

        Assert.Equal("TERMINAL_STATE", failure.Code);
        Assert.Equal(409, failure.StatusCode);
    }

    [Fact]
    public async Task Withdraw_by_requestor_closes_the_submission()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);

        var result = await NewService().DecideAsync(
            submission.Id, new DecideRequest("withdrawn", "No longer needed.", 1), TestUser.Employee("usr_amira"));

        Assert.Equal("withdrawn", result.Status);
        var row = await host.Db.Submissions.AsNoTracking().FirstAsync(s => s.Id == submission.Id);
        Assert.Equal("No longer needed.", row.DecisionNote);
        Assert.Equal(2, row.Version);
    }

    [Fact]
    public async Task Withdraw_locked_once_a_step_has_a_decision()
    {
        // Two-step template: after the supervisor discharges step 1 the
        // submission is in flight but a reviewer already acted — withdraw window closed.
        var submission = await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);
        var service = NewService();
        await service.DecideAsync(submission.Id, new DecideRequest("approved", "Queue ok.", 1), TestUser.Supervisor);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.DecideAsync(
                submission.Id, new DecideRequest("withdrawn", "I want it back.", 2), TestUser.Employee("usr_tarek")));

        Assert.Equal("WITHDRAW_TOO_LATE", failure.Code);
    }

    [Fact]
    public async Task Withdraw_by_anyone_else_is_an_ownership_violation()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().DecideAsync(
                submission.Id, new DecideRequest("withdrawn", null, 1), TestUser.Supervisor));

        Assert.Equal("NOT_SUBMISSION_OWNER", failure.Code);
    }

    [Fact]
    public async Task Decision_casing_and_unknown_words_lands_in_INVALID_DECISION()
    {
        var submission = await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().DecideAsync(
                submission.Id, new DecideRequest("MAYBE  LATER", null, 1), TestUser.Supervisor));

        Assert.Equal("INVALID_DECISION", failure.Code);
        Assert.Equal(400, failure.StatusCode);
    }

    [Fact]
    public async Task Missing_submission_is_not_found()
    {
        var failure = await Assert.ThrowsAsync<BusinessRuleException>(
            () => NewService().DecideAsync(9999, new DecideRequest("approved", null, 1), TestUser.Supervisor));

        Assert.Equal("SUBMISSION_NOT_FOUND", failure.Code);
        Assert.Equal(404, failure.StatusCode);
    }

    // ── workload ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Workload_counts_pending_steps_by_status_and_gate()
    {
        await SeededSubmissionAsync(seed.ExpenseTemplateId, seed.TarekId);   // supervisor + admin pending
        await SeededSubmissionAsync(seed.LeaveTemplateId, seed.AmiraId);     // supervisor pending

        var rows = await NewService().WorkloadAsync();

        // Grouped by (status, gate): supervisor queue first, then admin mirrors.
        var tuples = rows.Select(r => (r.Status, r.RoleRequired)).ToList();
        Assert.Equal(("submitted", "supervisor"), tuples[0]);
        Assert.Equal(("submitted", "admin"), tuples[1]);
        Assert.Equal(2, rows[0].PendingSubmissions);
        Assert.Equal(1, rows[1].PendingSubmissions);
    }
}

