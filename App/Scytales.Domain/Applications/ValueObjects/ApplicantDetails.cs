namespace Scytales.Domain.Applications.ValueObjects;

/// <summary>
/// The personal details exactly as submitted. Every field may be missing or invalid on purpose:
/// judging them is the job of the data-validation step, not of construction.
/// </summary>
public sealed record ApplicantDetails(
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? NationalId,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? LicenceCategory)
{
    /// <summary>Age in whole years on <paramref name="date"/>; null when the date of birth is missing.</summary>
    public int? AgeOn(DateOnly date)
    {
        if (DateOfBirth is not { } dob)
            return null;

        var age = date.Year - dob.Year;
        return dob > date.AddYears(-age) ? age - 1 : age;
    }
}
