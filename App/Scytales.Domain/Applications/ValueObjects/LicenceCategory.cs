namespace Scytales.Domain.Applications.ValueObjects;

/// <summary>EU driving licence categories (Directive 2006/126/EC).</summary>
public enum LicenceCategory
{
    AM, A1, A2, A,
    B1, B, BE,
    C1, C1E, C, CE,
    D1, D1E, D, DE
}

public static class LicenceCategories
{
    /// <summary>
    /// Case-insensitive, whitespace-tolerant parse. Rejects numeric input such as "5" and
    /// enum flag syntax such as "A1, A2", which <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> would combine.
    /// </summary>
    public static bool TryParse(string? value, out LicenceCategory category)
    {
        category = default;
        var trimmed = value?.Trim();

        return !string.IsNullOrEmpty(trimmed)
            && char.IsLetter(trimmed[0])
            && trimmed.All(char.IsLetterOrDigit)
            && Enum.TryParse(trimmed, ignoreCase: true, out category)
            && Enum.IsDefined(category);
    }
}
