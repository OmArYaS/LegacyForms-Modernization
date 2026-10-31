using Microsoft.EntityFrameworkCore.Storage;

using ModernForms.Application.Common;

namespace ModernForms.Infrastructure.Persistence;

/// <summary>
/// Provider-aware transaction host: relational providers use the
/// DbContext-attached transaction; in-memory demo providers get a no-op so
/// the same use-case code paths run in every environment.
/// </summary>
internal sealed class RelationalTransactionScope : ITransactionScope
{
    private readonly IDbContextTransaction _transaction;

    public RelationalTransactionScope(IDbContextTransaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    public async ValueTask DisposeAsync() => await _transaction.DisposeAsync();

    public async Task CommitAsync(CancellationToken cancellationToken = default) =>
        await _transaction.CommitAsync(cancellationToken);

    public async Task RollbackAsync(CancellationToken cancellationToken = default) =>
        await _transaction.RollbackAsync(cancellationToken);
}

/// <summary>No-op scope for providers without transactional support (tests).</summary>
internal sealed class NoOpTransactionScope : ITransactionScope
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
