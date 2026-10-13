using Scytales.Domain.Applications.Policies;

namespace Scytales.Domain.Tests.Applications.Policies;

public class WorkflowRulesTests
{
    [Fact]
    public void Defaults_are_consistent()
    {
        new WorkflowRules().Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(60)]
    [InlineData(20)]
    public void Review_threshold_must_be_above_the_failure_minimum(int reviewThreshold)
    {
        var rules = new WorkflowRules { Photo = new PhotoRules { MinQualityScore = 60, ManualReviewQualityThreshold = reviewThreshold } };

        rules.Validate().ShouldContain("ManualReviewQualityThreshold must be above MinQualityScore.");
    }

    [Fact]
    public void Review_age_must_not_be_below_the_minimum_age()
    {
        new WorkflowRules { MinimumAge = 18, ManualReviewBelowAge = 16 }
            .Validate().ShouldContain("ManualReviewBelowAge must not be below MinimumAge.");
    }

    [Fact]
    public void Every_inconsistency_is_reported()
    {
        var rules = new WorkflowRules
        {
            MinimumAge = -1,
            TimeZoneId = "Mars/Olympus_Mons",
            Photo = new PhotoRules
            {
                AllowedFormats = [],
                MinFileSizeBytes = -1,
                MinQualityScore = 150,
                ManualReviewQualityThreshold = -5
            }
        };

        rules.Validate().ShouldBe(
        [
            "MinimumAge must not be negative.",
            "TimeZoneId 'Mars/Olympus_Mons' is not a known time zone.",
            "AllowedFormats must contain at least one format.",
            "MinFileSizeBytes must not be negative.",
            "MinQualityScore must be between 0 and 100.",
            "ManualReviewQualityThreshold must be between 0 and 100.",
            "ManualReviewQualityThreshold must be above MinQualityScore."
        ]);
    }

    [Theory]
    [InlineData("Europe/Athens", "2026-06-09")] // 01:00 local
    [InlineData("UTC", "2026-06-08")]
    public void Reference_date_is_the_local_date_in_the_business_time_zone(string timeZoneId, string expected)
    {
        var instant = new DateTimeOffset(2026, 6, 8, 22, 0, 0, TimeSpan.Zero);

        new WorkflowRules { TimeZoneId = timeZoneId }.ReferenceDateFor(instant).ShouldBe(DateOnly.Parse(expected));
    }
}
