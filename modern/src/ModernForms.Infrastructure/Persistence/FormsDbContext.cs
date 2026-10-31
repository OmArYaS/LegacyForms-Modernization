using Microsoft.EntityFrameworkCore;

using ModernForms.Application.Common;
using ModernForms.Domain.Entities;

namespace ModernForms.Infrastructure.Persistence;

/// <summary>
/// EF Core persistence for the modern platform. Entity mapping keeps parity
/// with the legacy MySQL schema (database/mysql/01_schema.sql) — the Sqlite
/// demo profile is created from migrations; SqlServer/Azure SQL target the
/// same model; MySQL keeps running through the legacy procedures during
/// phase 1 and switches providers in phase 3 (see docs/migration-strategy.md).
/// </summary>
public sealed class FormsDbContext(DbContextOptions<FormsDbContext> options)
    : DbContext(options), IFormsDbContext
{
    public DbSet<FormTemplate> Templates => Set<FormTemplate>();

    public DbSet<FormSubmission> Submissions => Set<FormSubmission>();

    public DbSet<ApprovalStep> Steps => Set<ApprovalStep>();

    public DbSet<FormEvent> Events => Set<FormEvent>();

    public DbSet<Employee> Employees => Set<Employee>();

    private bool RelationalSupport =>
        Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;

    bool IFormsDbContext.SupportsTransactions => RelationalSupport;

    async Task<ITransactionScope> IFormsDbContext.BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!RelationalSupport)
        {
            return new NoOpTransactionScope();
        }

        var tx = await Database.BeginTransactionAsync(cancellationToken);
        return new RelationalTransactionScope(tx);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        FormsEntityConfigurations.ApplyAll(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}
