using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.Policies;

public class RiskAssessmentPolicyTests
{
    private const int ReviewThreshold = 60;

    private static RuleResult Evaluate(
        ApplicantDetails? applicant = null,
        int? score = GoodScore,
        bool isWatchlisted = false,
        IRiskAssessmentPolicy? policy = null) =>
        (policy ?? RiskPolicy).Evaluate(new RiskAssessmentInput(applicant ?? ValidApplicant(), Now, score, isWatchlisted));

    [Fact]
    public void No_risk_flags_means_auto_approval()
    {
        Evaluate().Passed.ShouldBeTrue();
    }

    [Fact]
    public void Below_21_requires_review()
    {
        Evaluate(AgedExactly(20)).Reasons.ShouldBe(["Applicant is below 21"]);
    }

    [Fact]
    public void Turning_21_on_the_reference_date_does_not()
    {
        Evaluate(AgedExactly(21)).Passed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("a1", "A1")]
    [InlineData("BE", "BE")]
    public void Categories_other_than_B_require_review(string category, string normalised)
    {
        Evaluate(ValidApplicant() with { LicenceCategory = category })
            .Reasons.ShouldBe([$"Licence category {normalised} requires manual review"]);
    }

    [Fact]
    public void Category_B_is_matched_regardless_of_case_and_spacing()
    {
        Evaluate(ValidApplicant() with { LicenceCategory = " b " }).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Auto_approved_categories_are_configurable()
    {
        var policy = new RiskAssessmentPolicy(new WorkflowRules { AutoApprovedCategories = [LicenceCategory.B, LicenceCategory.BE] });

        Evaluate(ValidApplicant() with { LicenceCategory = "BE" }, policy: policy).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Watchlisted_national_id_requires_review()
    {
        Evaluate(isWatchlisted: true).Reasons.ShouldBe(["National ID is on the watchlist"]);
    }

    [Fact]
    public void Score_below_the_review_threshold_requires_review()
    {
        Evaluate(score: ReviewThreshold - 1)
            .Reasons.ShouldBe([$"Photo quality score {ReviewThreshold - 1} is below the review threshold of {ReviewThreshold}"]);
    }

    [Fact]
    public void Score_at_the_review_threshold_does_not()
    {
        Evaluate(score: ReviewThreshold).Passed.ShouldBeTrue();
    }

    [Fact]
    public void All_reasons_are_reported_together()
    {
        var applicant = AgedExactly(19) with { LicenceCategory = "C" };

        Evaluate(applicant, score: 40, isWatchlisted: true).Reasons.ShouldBe(
        [
            "Applicant is below 21",
            "Licence category C requires manual review",
            "National ID is on the watchlist",
            $"Photo quality score 40 is below the review threshold of {ReviewThreshold}"
        ]);
    }

    [Fact]
    public void Age_uses_the_business_time_zone_date()
    {
        // 01:00 in Athens on the 21st birthday is still the day before in UTC.
        var submittedAt = new DateTimeOffset(2026, 6, 8, 22, 0, 0, TimeSpan.Zero);
        var applicant = ValidApplicant() with { DateOfBirth = new DateOnly(2005, 6, 9) };

        EvaluateAt(applicant, submittedAt).Passed.ShouldBeTrue();
        new RiskAssessmentPolicy(new WorkflowRules { TimeZoneId = "UTC" })
            .Evaluate(new RiskAssessmentInput(applicant, submittedAt, GoodScore, false))
            .Reasons.ShouldBe(["Applicant is below 21"]);
    }

    private static RuleResult EvaluateAt(ApplicantDetails applicant, DateTimeOffset submittedAt) =>
        RiskPolicy.Evaluate(new RiskAssessmentInput(applicant, submittedAt, GoodScore, IsWatchlisted: false));
}
