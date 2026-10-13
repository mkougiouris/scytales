using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

/// <summary>FR-5.3: any reason raised sends the application to manual review; none means auto-approval.</summary>
public interface IRiskAssessmentPolicy
{
    RuleResult Evaluate(RiskAssessmentInput input);
}

/// <param name="SubmittedAt">Age rules are judged on the submission date.</param>
/// <param name="IsWatchlisted">Looked up by the caller through the watchlist port.</param>
public sealed record RiskAssessmentInput(
    ApplicantDetails Applicant,
    DateTimeOffset SubmittedAt,
    int? PhotoQualityScore,
    bool IsWatchlisted);
