using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ModernForms.Domain.Entities;
using ModernForms.Domain.Enums;
using ModernForms.Domain.Schema;

namespace ModernForms.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds the demo dataset for the Sqlite profile so
/// the API is runnable out of the box (also the dataset the contract tests
/// and the local demo journey rely on — matches database/mysql/03_seed.sql).
/// </summary>
public sealed class DatabaseInitializer(FormsDbContext db, ILogger<DatabaseInitializer> logger)
{
#pragma warning disable CA1848 // startup logs run once per boot; verbosity over micro-perf
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        var hasData = await db.Templates.AnyAsync(cancellationToken);

        if (hasData)
        {
            return;
        }

        logger.LogInformation("Seeding demo data (employees, templates, sample submissions).");

        await db.Employees.AddRangeAsync(
            new List<Employee>
            {
                new Employee
                {
                    EmployeeKey = "usr_amira",
                    FullName = "Amira Hassan",
                    Email = "amira.hassan@contoso.internal",
                    Department = "Accounting",
                    Role = EmployeeRole.Employee,
                    Active = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Employee
                {
                    EmployeeKey = "usr_tarek",
                    FullName = "Tarek Fahmy",
                    Email = "tarek.fahmy@contoso.internal",
                    Department = "Support",
                    Role = EmployeeRole.Employee,
                    Active = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Employee
                {
                    EmployeeKey = "usr_mona",
                    FullName = "Mona Sharif",
                    Email = "mona.sharif@contoso.internal",
                    Department = "Operations",
                    Role = EmployeeRole.Supervisor,
                    Active = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Employee
                {
                    EmployeeKey = "usr_ossama",
                    FullName = "Ossama Nabil",
                    Email = "ossama.nabil@contoso.internal",
                    Department = "Finance",
                    Role = EmployeeRole.Admin,
                    Active = true,
                    CreatedAtUtc = DateTime.UtcNow
                }
            },
            cancellationToken);

        var now = DateTime.UtcNow;

        var templates = new List<FormTemplate>
        {
            new FormTemplate
            {
                TemplateKey = "leave-request",
                Title = "Leave request",
                Description = "Annual or sick leave — supervisor sign-off required.",
                FieldSchemaJson = SchemaParser.Serialize(new FieldSchemaDocument
                {
                    Fields =
                    [
                        new FormFieldDefinition { Key = "leaveType", Label = "Leave type", Type = "select", Required = true, Options = new List<string> { "Annual", "Sick", "Unpaid" } },
                        new FormFieldDefinition { Key = "fromDate", Label = "From", Type = "date", Required = true },
                        new FormFieldDefinition { Key = "toDate", Label = "To", Type = "date", Required = true },
                        new FormFieldDefinition { Key = "reason", Label = "Reason", Type = "multiline", Required = true, MaxLength = 500 }
                    ],
                    Workflow = new WorkflowSection
                    {
                        Steps = new List<WorkflowStepDefinition>
                        {
                            new() { Sequence = 1, Role = "supervisor" }
                        }
                    }
                }),
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new FormTemplate
            {
                TemplateKey = "expense-claim",
                Title = "Expense claim",
                Description = "Reimbursement — supervisor then finance (admin) sign-off.",
                FieldSchemaJson = SchemaParser.Serialize(new FieldSchemaDocument
                {
                    Fields =
                    [
                        new FormFieldDefinition { Key = "category", Label = "Category", Type = "select", Required = true, Options = new List<string> { "Travel", "Meals", "Equipment", "Other" } },
                        new FormFieldDefinition { Key = "amount", Label = "Amount (EGP)", Type = "number", Required = true, Min = 0 },
                        new FormFieldDefinition { Key = "receiptRef", Label = "Receipt reference", Type = "text", Required = true, MaxLength = 60 },
                        new FormFieldDefinition { Key = "notes", Label = "Notes", Type = "multiline", Required = false, MaxLength = 500 }
                    ],
                    Workflow = new WorkflowSection
                    {
                        Steps = new List<WorkflowStepDefinition>
                        {
                            new() { Sequence = 1, Role = "supervisor" },
                            new() { Sequence = 2, Role = "admin" }
                        }
                    }
                }),
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new FormTemplate
            {
                TemplateKey = "equipment-request",
                Title = "Equipment request",
                Description = "Hardware or software request — one-step supervisor approval.",
                FieldSchemaJson = SchemaParser.Serialize(new FieldSchemaDocument
                {
                    Fields =
                    [
                        new FormFieldDefinition { Key = "item", Label = "Item", Type = "text", Required = true, MaxLength = 200 },
                        new FormFieldDefinition { Key = "justification", Label = "Business justification", Type = "multiline", Required = true, MaxLength = 500 }
                    ],
                    Workflow = new WorkflowSection
                    {
                        Steps = new List<WorkflowStepDefinition>
                        {
                            new() { Sequence = 1, Role = "supervisor" }
                        }
                    }
                }),
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }
        };

        await db.Templates.AddRangeAsync(templates, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await SeedSampleSubmissionsAsync(cancellationToken);
        logger.LogInformation("Demo seed complete.");
    }
#pragma warning restore CA1848

    /// <summary>One of every workflow state, mirroring database/mysql/03_seed.sql.</summary>
    private async Task SeedSampleSubmissionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var amira = await db.Employees.FirstAsync(e => e.EmployeeKey == "usr_amira", cancellationToken);
        var tarek = await db.Employees.FirstAsync(e => e.EmployeeKey == "usr_tarek", cancellationToken);
        var mona = await db.Employees.FirstAsync(e => e.EmployeeKey == "usr_mona", cancellationToken);
        var ossama = await db.Employees.FirstAsync(e => e.EmployeeKey == "usr_ossama", cancellationToken);

        var leaveTemplate = await db.Templates.FirstAsync(t => t.TemplateKey == "leave-request", cancellationToken);
        var expenseTemplate = await db.Templates.FirstAsync(t => t.TemplateKey == "expense-claim", cancellationToken);
        var equipmentTemplate = await db.Templates.FirstAsync(t => t.TemplateKey == "equipment-request", cancellationToken);

        // 1 — leave request, fully approved
        var approved = await CreateSubmissionAsync(leaveTemplate, amira,
            new { leaveType = "Annual", fromDate = "2026-05-11", toDate = "2026-05-15", reason = "Family trip" },
            submitNow: true, now, cancellationToken);
        await DecideStepAsync(approved, mona, StepDecision.Approved, "Enjoy — working days covered.", now, cancellationToken);

        // 2 — expense claim awaiting supervisor
        await CreateSubmissionAsync(expenseTemplate, tarek,
            new { category = "Travel", amount = 1840.50f, receiptRef = "TRV-2026-0042", notes = "Client visit." },
            submitNow: true, now, cancellationToken);

        // 3 — equipment request rejected
        var rejected = await CreateSubmissionAsync(equipmentTemplate, tarek,
            new { item = "42\" television", justification = "To watch training videos in the break room." },
            submitNow: true, now, cancellationToken);
        await DecideStepAsync(rejected, mona, StepDecision.Rejected, "Not business-critical this quarter.", now, cancellationToken);

        // 4 — leave request still a draft
        await CreateSubmissionAsync(leaveTemplate, amira,
            new { leaveType = "Sick", fromDate = "2026-06-01", toDate = "2026-06-02", reason = "Doctor visit" },
            submitNow: false, now, cancellationToken);
    }

    private async Task<FormSubmission> CreateSubmissionAsync(
        FormTemplate template, Employee requestor, object payload, bool submitNow, DateTime now, CancellationToken ct)
    {
        var submission = new FormSubmission
        {
            TemplateId = template.Id,
            RequestorId = requestor.Id,
            Status = submitNow ? FormStatus.Submitted : FormStatus.Draft,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
            SubmittedAtUtc = submitNow ? now : null,
            Version = 1,
            CreatedAtUtc = now
        };

        // Persist the header first so the generated id is available for children.
        await db.Submissions.AddAsync(submission, ct);
        await db.SaveChangesAsync(ct);

        await db.Events.AddAsync(new FormEvent
        {
            SubmissionId = submission.Id,
            EventType = "created",
            ActorId = requestor.Id,
            Detail = submitNow ? "submitted" : "draft",
            OccurredAtUtc = now
        }, ct);

        if (submitNow)
        {
            var schema = SchemaParser.Parse(template.FieldSchemaJson);

            foreach (var step in schema.Workflow.Steps.OrderBy(s => s.Sequence))
            {
                await db.Steps.AddAsync(new ApprovalStep
                {
                    SubmissionId = submission.Id,
                    StepSequence = step.Sequence,
                    RoleRequired = step.Role == "admin" ? ApprovalRole.Admin : ApprovalRole.Supervisor,
                    Decision = StepDecision.Pending
                }, ct);
            }

            await db.Events.AddAsync(new FormEvent
            {
                SubmissionId = submission.Id,
                EventType = "submitted",
                ActorId = requestor.Id,
                Detail = null,
                OccurredAtUtc = now
            }, ct);
        }

        await db.SaveChangesAsync(ct);
        return submission;
    }

    private async Task DecideStepAsync(
        FormSubmission submission, Employee decider, StepDecision decision, string note, DateTime now, CancellationToken ct)
    {
        var step = await db.Steps
            .Where(st => st.SubmissionId == submission.Id && st.Decision == StepDecision.Pending)
            .OrderBy(st => st.StepSequence)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Seeded submission lost its pending step.");

        step.Decision = decision;
        step.DecidedById = decider.Id;
        step.DecidedAtUtc = now;
        step.Note = note;

        if (decision == StepDecision.Rejected)
        {
            submission.Status = FormStatus.Rejected;
            submission.ClosedAtUtc = now;
            submission.DecisionNote = note;
        }
        else
        {
            var remaining = await db.Steps.CountAsync(st =>
                st.SubmissionId == submission.Id && st.Id != step.Id && st.Decision == StepDecision.Pending, ct);

            if (remaining == 0)
            {
                submission.Status = FormStatus.Approved;
                submission.ClosedAtUtc = now;
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
            ActorId = decider.Id,
            Detail = $"step {step.StepSequence}",
            OccurredAtUtc = now
        }, ct);

        await db.SaveChangesAsync(ct);
    }
}
