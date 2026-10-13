using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Application.Abstractions;

/// <summary>
/// Port to the (simulated) photo quality service. Implementations must be deterministic
/// (same photo, same score) so a redelivered message reaches the same outcome.
/// </summary>
public interface IPhotoQualityScorer
{
    /// <returns>A score from 0 (unusable) to 100 (perfect).</returns>
    Task<int> ScoreAsync(ApplicationPhoto photo, CancellationToken cancellationToken);
}
