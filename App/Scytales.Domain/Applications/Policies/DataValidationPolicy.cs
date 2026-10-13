using System.Text.RegularExpressions;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

public sealed partial class DataValidationPolicy(WorkflowRules rules) : IDataValidationPolicy
{
    public RuleResult Evaluate(ApplicantDetails applicant, DateTimeOffset submittedAt)
    {
        var referenceDate = rules.ReferenceDateFor(submittedAt);
        List<string> reasons = [];

        Require(applicant.FirstName, "First name", reasons);
        Require(applicant.LastName, "Last name", reasons);
        if (applicant.DateOfBirth is null)
            reasons.Add("Date of birth is required");
        if (string.IsNullOrWhiteSpace(applicant.NationalId))
            reasons.Add("National ID is missing");
        Require(applicant.Email, "Email", reasons);
        Require(applicant.PhoneNumber, "Phone number", reasons);
        Require(applicant.Address, "Address", reasons);
        Require(applicant.LicenceCategory, "Licence category", reasons);

        if (!string.IsNullOrWhiteSpace(applicant.Email) && !EmailPattern().IsMatch(applicant.Email.Trim()))
            reasons.Add("Email is invalid");

        if (!string.IsNullOrWhiteSpace(applicant.LicenceCategory) && !LicenceCategories.TryParse(applicant.LicenceCategory, out _))
            reasons.Add($"Licence category '{applicant.LicenceCategory.Trim()}' is not recognised");

        if (applicant.AgeOn(referenceDate) is { } age && age < rules.MinimumAge)
            reasons.Add($"Applicant is under {rules.MinimumAge}");

        return new RuleResult(reasons);
    }

    private static void Require(string? value, string field, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(value))
            reasons.Add($"{field} is required");
    }

    // Deliberately simple: one '@', no whitespace, a dot in the domain part.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
