namespace ModernForms.Application.Common;

/// <summary>
/// Commits on successful disposal; rolls back on exception or explicit
/// RollbackAsync. The unit of work for multi-write use cases such as the
/// decision transition, which touches steps, the header and the audit trail.
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
