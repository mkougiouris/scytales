using Microsoft.EntityFrameworkCore;
using Scytales.Application.Abstractions;

namespace Scytales.Infrastructure.Persistence;

internal sealed class ApplicationRepository(ScytalesDbContext db) : IApplicationRepository
{
    // Owned applicant details and photo load with the aggregate; history does not.
    public Task<LicenceApplication?> GetAsync(Guid applicationId, CancellationToken cancellationToken) =>
        db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

    // A scalar projection: no tracking, and the owned tables (and the photo) are never joined.
    public Task<IdempotentSubmission?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Applications
            .AsNoTracking()
            .Where(a => a.IdempotencyKey!.Key == idempotencyKey)
            .Select(a => new IdempotentSubmission(a.Id, a.Status, a.IdempotencyKey!.RequestHash))
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(LicenceApplication application) => db.Applications.Add(application);
}
