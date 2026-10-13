using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.ValueObjects;

public class ApplicantDetailsTests
{
    [Theory]
    [InlineData("2008-06-09", "2026-06-09", 18)] // birthday today
    [InlineData("2008-06-10", "2026-06-09", 17)] // birthday tomorrow
    [InlineData("2008-02-29", "2026-02-28", 17)] // leap-day birth, non-leap year
    [InlineData("2008-02-29", "2026-03-01", 18)]
    [InlineData("2026-06-10", "2026-06-09", -1)] // not born yet
    public void AgeOn_counts_whole_years(string dateOfBirth, string on, int expected)
    {
        var applicant = ValidApplicant() with { DateOfBirth = DateOnly.Parse(dateOfBirth) };

        applicant.AgeOn(DateOnly.Parse(on)).ShouldBe(expected);
    }

    [Fact]
    public void AgeOn_is_null_without_a_date_of_birth()
    {
        (ValidApplicant() with { DateOfBirth = null }).AgeOn(Today).ShouldBeNull();
    }
}
