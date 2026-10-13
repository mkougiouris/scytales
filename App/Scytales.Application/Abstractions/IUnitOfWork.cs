using Scytales.Application.Exceptions;

namespace Scytales.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Commits every tracked change, including messages handed to <see cref="IMessagePublisher"/>, in one transaction.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">The aggregate was changed by someone else since it was loaded.</exception>
    /// <exception cref="DuplicateIdempotencyKeyException">A new application reused an idempotency key already stored.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
