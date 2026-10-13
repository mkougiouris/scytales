using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

public sealed class RiskAssessmentPolicy(WorkflowRules rules) : IRiskAssessmentPolicy
{
    public RuleResult Evaluate(RiskAssessmentInput input)
    {
        var applicant = input.Applicant;
        List<string> reasons = [];

        if (applicant.AgeOn(rules.ReferenceDateFor(input.SubmittedAt)) is { } age && age < rules.ManualReviewBelowAge)
            reasons.Add($"Applicant is below {rules.ManualReviewBelowAge}");

        if (!LicenceCategories.TryParse(applicant.LicenceCategory, out var category))
            reasons.Add($"Licence category '{applicant.LicenceCategory?.Trim()}' is not recognised");
        else if (!rules.AutoApprovedCategories.Contains(category))
            reasons.Add($"Licence category {category} requires manual review");

        if (input.IsWatchlisted)
            reasons.Add("National ID is on the watchlist");

        if (input.PhotoQualityScore < rules.Photo.ManualReviewQualityThreshold)
            reasons.Add($"Photo quality score {input.PhotoQualityScore} is below the review threshold of {rules.Photo.ManualReviewQualityThreshold}");

        return new RuleResult(reasons);
    }
}
