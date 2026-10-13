namespace Scytales.Application.Queries;

/// <summary>
/// Read side: projections straight from storage, without loading the aggregate (and so never the photo).
/// </summary>
public interface IApplicationQueries
{
    /// <returns>Null when the application does not exist.</returns>
    Task<ApplicationStatusDto?> GetStatusAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <returns>Oldest entry first; null when the application does not exist.</returns>
    Task<IReadOnlyList<ApplicationHistoryEntryDto>?> GetHistoryAsync(Guid applicationId, CancellationToken cancellationToken);
}
