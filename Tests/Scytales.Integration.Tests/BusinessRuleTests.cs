namespace Scytales.Integration.Tests;

/// <summary>FR-5.1 and FR-5.3 through the real worker, with the rules as configured in appsettings.</summary>
public sealed class BusinessRuleTests(ScytalesSystem system)
{
    private readonly HttpClient _api = system.Api;

    // A week's margin keeps the ages clear of the configured time zone's date boundary.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly string Aged17 = Today.AddYears(-18).AddDays(7).ToString("yyyy-MM-dd");
    private static readonly string Aged19 = Today.AddYears(-20).AddDays(7).ToString("yyyy-MM-dd");

    [Theory]
    [InlineData("email", "not-an-email", "Failed", "Email is invalid")]
    [InlineData("nationalId", null, "Failed", "National ID is missing")]
    [InlineData("dateOfBirth", "aged 17", "Failed", "Applicant is under 18")]
    [InlineData("dateOfBirth", "aged 19", "PendingManualReview", "Applicant is below 21")]
    [InlineData("licenceCategory", "A", "PendingManualReview", "Licence category A requires manual review")]
    [InlineData("nationalId", ApiClient.WatchlistedNationalId, "PendingManualReview", "National ID is on the watchlist")]
    public async Task Applicant_details_lead_to_the_expected_outcome(string field, string? value, string expectedStatus, string expectedDetails)
    {
        value = value switch { "aged 17" => Aged17, "aged 19" => Aged19, _ => value };

        var id = await _api.SubmitAndGetIdAsync(fields: new Dictionary<string, string?> { [field] = value });

        (await _api.WaitForOutcomeAsync(id)).Status.ShouldBe(expectedStatus);
        (await _api.GetHistoryAsync(id))[^1].Details.ShouldBe(expectedDetails);
    }
}
