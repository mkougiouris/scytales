using Microsoft.EntityFrameworkCore;
using Scytales.Application.Queries;

namespace Scytales.Infrastructure.Persistence;

/// <summary>Read side: no tracking, scalar projections only, so the owned tables (and the photo) are never joined.</summary>
internal sealed class ApplicationQueries(ScytalesDbContext db) : IApplicationQueries
{
    public Task<ApplicationStatusDto?> GetStatusAsync(Guid applicationId, CancellationToken cancellationToken) =>
        db.Applications
            .AsNoTracking()
            .Where(a => a.Id == applicationId)
            .Select(a => new ApplicationStatusDto(a.Id, a.Status, a.CreatedAt, a.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ApplicationHistoryEntryDto>?> GetHistoryAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entries = await db.ApplicationHistory
            .AsNoTracking()
            .Where(e => e.ApplicationId == applicationId)
            .OrderBy(e => e.Version)
            .Select(e => new ApplicationHistoryEntryDto(e.Timestamp, e.FromStatus, e.ToStatus, e.Event, e.Details))
            .ToListAsync(cancellationToken);

        // Every application has at least its ApplicationCreated entry, so no entries means no application.
        return entries.Count == 0 ? null : entries;
    }
}
