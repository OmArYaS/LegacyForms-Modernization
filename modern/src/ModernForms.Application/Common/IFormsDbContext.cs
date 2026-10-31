using ModernForms.Domain.Entities;

namespace ModernForms.Application.Common;

/// <summary>
/// Persistence surface of the Application layer: the entity sets the forms
/// module touches, the save step, and a provider-aware transaction host —
/// providers without transactional support (InMemory tests) swap in a no-op.
/// </summary>
public interface IFormsDbContext
{
    Microsoft.EntityFrameworkCore.DbSet<FormTemplate> Templates { get; }
    Microsoft.EntityFrameworkCore.DbSet<FormSubmission> Submissions { get; }
    Microsoft.EntityFrameworkCore.DbSet<ApprovalStep> Steps { get; }
    Microsoft.EntityFrameworkCore.DbSet<FormEvent> Events { get; }
    Microsoft.EntityFrameworkCore.DbSet<Employee> Employees { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether the current provider supports relational transactions.</summary>
    bool SupportsTransactions { get; }

    /// <summary>Begins a scope that commits on dispose (no-op for InMemory).</summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
