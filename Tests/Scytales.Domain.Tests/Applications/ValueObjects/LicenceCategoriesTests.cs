using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.ValueObjects;

public class LicenceCategoriesTests
{
    [Theory]
    [InlineData("B", LicenceCategory.B)]
    [InlineData("b", LicenceCategory.B)]
    [InlineData(" c1e ", LicenceCategory.C1E)]
    [InlineData("AM", LicenceCategory.AM)]
    public void Parses_known_categories(string value, LicenceCategory expected)
    {
        LicenceCategories.TryParse(value, out var category).ShouldBeTrue();
        category.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Z")]
    [InlineData("B2")]
    [InlineData("5")]
    [InlineData("1")]
    [InlineData("A1, A2")] // enum flag syntax would otherwise combine into A
    [InlineData("B,BE")]
    public void Rejects_anything_else(string? value)
    {
        LicenceCategories.TryParse(value, out _).ShouldBeFalse();
    }
}
