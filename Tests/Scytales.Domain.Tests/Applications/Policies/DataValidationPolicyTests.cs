using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.Policies;

public class DataValidationPolicyTests
{
    private static RuleResult Evaluate(ApplicantDetails applicant) => DataPolicy.Evaluate(applicant, Now);

    [Fact]
    public void A_complete_adult_application_passes()
    {
        var result = Evaluate(ValidApplicant());

        result.Passed.ShouldBeTrue();
        result.Summary.ShouldBeNull();
    }

    [Theory]
    [InlineData("FirstName", "First name is required")]
    [InlineData("LastName", "Last name is required")]
    [InlineData("DateOfBirth", "Date of birth is required")]
    [InlineData("NationalId", "National ID is missing")]
    [InlineData("Email", "Email is required")]
    [InlineData("PhoneNumber", "Phone number is required")]
    [InlineData("Address", "Address is required")]
    [InlineData("LicenceCategory", "Licence category is required")]
    public void Every_field_is_required(string field, string expectedReason)
    {
        var applicant = Without(field);

        Evaluate(applicant).Reasons.ShouldBe([expectedReason]);
    }

    [Fact]
    public void Whitespace_counts_as_missing()
    {
        Evaluate(ValidApplicant() with { FirstName = "   " }).Reasons.ShouldBe(["First name is required"]);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("john@example")]
    [InlineData("john doe@example.com")]
    [InlineData("@example.com")]
    [InlineData("john@@example.com")]
    public void Invalid_email_fails(string email)
    {
        Evaluate(ValidApplicant() with { Email = email }).Reasons.ShouldBe(["Email is invalid"]);
    }

    [Fact]
    public void Email_is_trimmed_before_checking()
    {
        Evaluate(ValidApplicant() with { Email = "  john.doe@example.com " }).Passed.ShouldBeTrue();
    }

    // What counts as a known category is LicenceCategoriesTests' job; this checks the policy reports it.
    [Fact]
    public void Unknown_licence_category_fails()
    {
        Evaluate(ValidApplicant() with { LicenceCategory = "Z" })
            .Reasons.ShouldBe(["Licence category 'Z' is not recognised"]);
    }

    [Theory]
    [InlineData("b")]
    [InlineData(" C1E ")]
    public void Known_categories_pass_regardless_of_case_and_spacing(string category)
    {
        Evaluate(ValidApplicant() with { LicenceCategory = category }).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Turning_18_on_the_reference_date_passes()
    {
        Evaluate(AgedExactly(18)).Passed.ShouldBeTrue();
    }

    [Fact]
    public void One_day_short_of_18_fails()
    {
        var applicant = ValidApplicant() with { DateOfBirth = Today.AddYears(-18).AddDays(1) };

        Evaluate(applicant).Reasons.ShouldBe(["Applicant is under 18"]);
    }

    [Fact]
    public void A_future_date_of_birth_fails()
    {
        Evaluate(ValidApplicant() with { DateOfBirth = Today.AddDays(1) }).Reasons.ShouldBe(["Applicant is under 18"]);
    }

    [Fact]
    public void Age_uses_the_business_time_zone_date()
    {
        // 01:00 in Athens on the 18th birthday is still the day before in UTC.
        var submittedAt = new DateTimeOffset(2026, 6, 8, 22, 0, 0, TimeSpan.Zero);
        var applicant = ValidApplicant() with { DateOfBirth = new DateOnly(2008, 6, 9) };

        DataPolicy.Evaluate(applicant, submittedAt).Passed.ShouldBeTrue();
        new DataValidationPolicy(new WorkflowRules { TimeZoneId = "UTC" })
            .Evaluate(applicant, submittedAt).Reasons.ShouldBe(["Applicant is under 18"]);
    }

    [Fact]
    public void Minimum_age_is_configurable()
    {
        var policy = new DataValidationPolicy(new WorkflowRules { MinimumAge = 17, ManualReviewBelowAge = 17 });

        policy.Evaluate(AgedExactly(17), Now).Passed.ShouldBeTrue();
    }

    [Fact]
    public void All_reasons_are_reported_together()
    {
        var applicant = ValidApplicant() with
        {
            NationalId = null,
            Email = "nope",
            LicenceCategory = "Z",
            DateOfBirth = Today.AddYears(-10)
        };

        var result = Evaluate(applicant);

        result.Reasons.ShouldBe(
        [
            "National ID is missing",
            "Email is invalid",
            "Licence category 'Z' is not recognised",
            "Applicant is under 18"
        ]);
        result.Summary.ShouldBe("National ID is missing; Email is invalid; Licence category 'Z' is not recognised; Applicant is under 18");
    }

    private static ApplicantDetails Without(string field)
    {
        var valid = ValidApplicant();
        return field switch
        {
            "FirstName" => valid with { FirstName = null },
            "LastName" => valid with { LastName = null },
            "DateOfBirth" => valid with { DateOfBirth = null },
            "NationalId" => valid with { NationalId = null },
            "Email" => valid with { Email = null },
            "PhoneNumber" => valid with { PhoneNumber = null },
            "Address" => valid with { Address = null },
            "LicenceCategory" => valid with { LicenceCategory = null },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };
    }
}
