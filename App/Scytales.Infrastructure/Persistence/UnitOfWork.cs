using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;
using Scytales.Infrastructure.Persistence.Configurations;

namespace Scytales.Infrastructure.Persistence;

internal sealed class UnitOfWork(ScytalesDbContext db) : IUnitOfWork
{
    private const string ConflictMessage = "The application was changed by someone else; reload and try again.";

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The Version token no longer matched (D-4).
            throw new ConcurrencyConflictException(ConflictMessage, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ApplicationConfiguration.IdempotencyKeyIndex
        })
        {
            // A concurrent submission with the same Idempotency-Key committed first (BN-2).
            throw new DuplicateIdempotencyKeyException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent writer already recorded a history entry for this version.
            throw new ConcurrencyConflictException(ConflictMessage, ex);
        }
    }
}
