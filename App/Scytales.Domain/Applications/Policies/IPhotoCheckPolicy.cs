using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

/// <summary>FR-5.2: any reason raised sends the application to <c>Failed</c>.</summary>
public interface IPhotoCheckPolicy
{
    /// <param name="photo">Null when none was uploaded.</param>
    /// <param name="qualityScore">0–100; null exactly when <paramref name="photo"/> is null.</param>
    RuleResult Evaluate(ApplicationPhoto? photo, int? qualityScore);
}
