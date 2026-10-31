using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ModernForms.Domain.Entities;
using ModernForms.Domain.Enums;

namespace ModernForms.Infrastructure.Persistence;

/// <summary>
/// Entity mappings. Names and value ranges are chosen deliberately to keep
/// a 1:1 physical story with the legacy MySQL schema (form_template,
/// form_submission, approval_step, form_event, employee), so phase 1 of the
/// migration bridges data rather than translating it.
/// </summary>
public sealed class FormsEntityConfigurations
{
    internal static void ApplyAll(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FormTemplate>(ConfigureTemplate);
        modelBuilder.Entity<FormSubmission>(ConfigureSubmission);
        modelBuilder.Entity<ApprovalStep>(ConfigureStep);
        modelBuilder.Entity<FormEvent>(ConfigureEvent);
        modelBuilder.Entity<Employee>(ConfigureEmployee);
    }

    private static void ConfigureTemplate(EntityTypeBuilder<FormTemplate> template)
    {
        template.ToTable("form_template");
        template.HasKey(t => t.Id);
        template.Property(t => t.Id).HasColumnName("template_id").ValueGeneratedOnAdd();
        template.Property(t => t.TemplateKey).HasColumnName("template_key").HasMaxLength(64).IsRequired();
        template.HasIndex(t => t.TemplateKey).IsUnique();
        template.Property(t => t.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        template.Property(t => t.Description).HasColumnName("description").HasMaxLength(500);
        template.Property(t => t.FieldSchemaJson).HasColumnName("field_schema").IsRequired();
        template.Property(t => t.IsActive).HasColumnName("is_active");
        template.Property(t => t.CreatedAtUtc).HasColumnName("created_at");
        template.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at");
    }

    private static void ConfigureSubmission(EntityTypeBuilder<FormSubmission> submission)
    {
        submission.ToTable("form_submission");
        submission.HasKey(s => s.Id);
        submission.Property(s => s.Id).HasColumnName("submission_id").ValueGeneratedOnAdd();
        submission.Property(s => s.TemplateId).HasColumnName("template_id");
        submission.Property(s => s.RequestorId).HasColumnName("requestor_id");
        submission.Property(s => s.Status)
            .HasColumnName("status")
            // Stored lowercase — byte-parity with the MySQL ENUM('draft','submitted',...).
            .HasConversion(e => e.ToString().ToLowerInvariant(), value => ParseEnumOr(value, FormStatus.Draft))
            .HasMaxLength(16)
            .IsRequired();
        submission.Property(s => s.PayloadJson).HasColumnName("payload_json").IsRequired();
        submission.Property(s => s.SubmittedAtUtc).HasColumnName("submitted_at");
        submission.Property(s => s.ClosedAtUtc).HasColumnName("closed_at");
        submission.Property(s => s.DecisionNote).HasColumnName("decision_note").HasMaxLength(500);
        submission.Property(s => s.Version).HasColumnName("version");
        // Optimistic concurrency: two decision writers on one stale version
        // collide in the store, surfacing DbUpdateConcurrencyException → 409.
        submission.Property(s => s.Version).IsConcurrencyToken();
        submission.Property(s => s.CreatedAtUtc).HasColumnName("created_at");

        submission.HasIndex(s => new { s.Status, s.SubmittedAtUtc }).HasDatabaseName("ix_submission_status_submitted");
        submission.HasIndex(s => s.RequestorId).HasDatabaseName("ix_submission_requestor");

        submission.HasOne(s => s.Template)
            .WithMany()
            .HasForeignKey(s => s.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        submission.HasOne(s => s.Requestor)
            .WithMany()
            .HasForeignKey(s => s.RequestorId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureStep(EntityTypeBuilder<ApprovalStep> step)
    {
        step.ToTable("approval_step");
        step.HasKey(s => s.Id);
        step.Property(s => s.Id).HasColumnName("step_id").ValueGeneratedOnAdd();
        step.Property(s => s.SubmissionId).HasColumnName("submission_id");
        step.Property(s => s.StepSequence).HasColumnName("step_sequence");
        step.Property(s => s.RoleRequired)
            .HasColumnName("role_required")
            .HasConversion(e => e.ToString().ToLowerInvariant(), value => ParseEnumOr(value, ApprovalRole.Supervisor))
            .HasMaxLength(16)
            .IsRequired();
        step.Property(s => s.Decision)
            .HasColumnName("decision")
            .HasConversion(e => e.ToString().ToLowerInvariant(), value => ParseEnumOr(value, StepDecision.Pending))
            .HasMaxLength(16)
            .IsRequired();
        step.Property(s => s.DecidedById).HasColumnName("decided_by");
        step.Property(s => s.DecidedAtUtc).HasColumnName("decided_at");
        step.Property(s => s.Note).HasColumnName("note").HasMaxLength(500);

        step.HasIndex(s => new { s.SubmissionId, s.StepSequence }).IsUnique();
        step.HasIndex(s => new { s.RoleRequired, s.Decision }).HasDatabaseName("ix_step_role_decision");

        step.HasOne(s => s.Submission)
            .WithMany()
            .HasForeignKey(s => s.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optimistic concurrency on the parent header: two supervisors saving
        // from stale clients raise DbUpdateConcurrencyException (→ HTTP 409).
        step.HasOne(s => s.DecidedBy)
            .WithMany()
            .HasForeignKey(s => s.DecidedById)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureEvent(EntityTypeBuilder<FormEvent> ev)
    {
        ev.ToTable("form_event");
        ev.HasKey(e => e.Id);
        ev.Property(e => e.Id).HasColumnName("event_id").ValueGeneratedOnAdd();
        ev.Property(e => e.SubmissionId).HasColumnName("submission_id");
        ev.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
        ev.Property(e => e.ActorId).HasColumnName("actor_id");
        ev.Property(e => e.Detail).HasColumnName("detail").HasMaxLength(400);
        ev.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at");

        ev.HasIndex(e => new { e.SubmissionId, e.OccurredAtUtc }).HasDatabaseName("ix_event_submission");

        ev.HasOne(e => e.Submission)
            .WithMany()
            .HasForeignKey(e => e.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureEmployee(EntityTypeBuilder<Employee> employee)
    {
        employee.ToTable("employee");
        employee.HasKey(e => e.Id);
        employee.Property(e => e.Id).HasColumnName("employee_id").ValueGeneratedOnAdd();
        employee.Property(e => e.EmployeeKey).HasColumnName("employee_key").HasMaxLength(64).IsRequired();
        employee.HasIndex(e => e.EmployeeKey).IsUnique();
        employee.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(160).IsRequired();
        employee.Property(e => e.Email).HasColumnName("email").HasMaxLength(190);
        employee.Property(e => e.Department).HasColumnName("department").HasMaxLength(80);
        employee.Property(e => e.Role)
            .HasColumnName("role")
            .HasConversion(e => e.ToString().ToLowerInvariant(), value => ParseEmployeeRole(value))
            .HasMaxLength(16)
            .IsRequired();
        employee.Property(e => e.Active).HasColumnName("active");
        employee.Property(e => e.CreatedAtUtc).HasColumnName("created_at");

        employee.HasIndex(e => e.Role).HasDatabaseName("ix_employee_role");
    }

    private static T ParseEnumOr<T>(string value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static EmployeeRole ParseEmployeeRole(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "employee" => EmployeeRole.Employee,
            "supervisor" => EmployeeRole.Supervisor,
            "admin" => EmployeeRole.Admin,
            _ => EmployeeRole.Employee
        };
    }
}
