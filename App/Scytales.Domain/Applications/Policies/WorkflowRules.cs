using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

/// <summary>
/// The configurable thresholds the default policies apply (D-6). Plain values only: binding them from
/// configuration is the host's job. Reference data such as the watchlist is not a threshold and
/// comes in through an Application-layer port instead.
/// </summary>
public sealed record WorkflowRules
{
    /// <summary>Below this age, data validation fails (FR-5.1.4).</summary>
    public int MinimumAge { get; init; } = 18;

    /// <summary>Below this age, the application goes to manual review (FR-5.3.1).</summary>
    public int ManualReviewBelowAge { get; init; } = 21;

    /// <summary>Any other category goes to manual review (FR-5.3.2).</summary>
    public IReadOnlyCollection<LicenceCategory> AutoApprovedCategories { get; init; } = [LicenceCategory.B];

    /// <summary>
    /// IANA time zone of the issuing authority. Age rules use the submission's local calendar date there,
    /// so a birthday counts from local midnight rather than UTC midnight.
    /// </summary>
    public string TimeZoneId { get; init; } = "Europe/Athens";

    public PhotoRules Photo { get; init; } = new();

    /// <summary>The local calendar date of <paramref name="instant"/> in <see cref="TimeZoneId"/>.</summary>
    public DateOnly ReferenceDateFor(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).DateTime);

    /// <summary>Returns every inconsistency; empty when the rules are usable.</summary>
    public IEnumerable<string> Validate()
    {
        if (MinimumAge < 0)
            yield return $"{nameof(MinimumAge)} must not be negative.";
        if (ManualReviewBelowAge < MinimumAge)
            yield return $"{nameof(ManualReviewBelowAge)} must not be below {nameof(MinimumAge)}.";
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId ?? string.Empty, out _))
            yield return $"{nameof(TimeZoneId)} '{TimeZoneId}' is not a known time zone.";

        foreach (var error in Photo.Validate())
            yield return error;
    }
}

public sealed record PhotoRules
{
    /// <summary>Formats accepted, judged by magic bytes (FR-5.2.2).</summary>
    public IReadOnlyCollection<PhotoFormat> AllowedFormats { get; init; } = [PhotoFormat.Jpeg, PhotoFormat.Png];

    /// <summary>Smaller files fail the photo check (FR-5.2.3).</summary>
    public long MinFileSizeBytes { get; init; } = 10 * 1024;

    /// <summary>Lower scores fail the photo check (FR-5.2.4).</summary>
    public int MinQualityScore { get; init; } = 30;

    /// <summary>Lower scores go to manual review (FR-5.3.4). Must be above <see cref="MinQualityScore"/>.</summary>
    public int ManualReviewQualityThreshold { get; init; } = 60;

    public IEnumerable<string> Validate()
    {
        if (AllowedFormats.Count == 0)
            yield return $"{nameof(AllowedFormats)} must contain at least one format.";
        if (MinFileSizeBytes < 0)
            yield return $"{nameof(MinFileSizeBytes)} must not be negative.";
        if (MinQualityScore is < 0 or > 100)
            yield return $"{nameof(MinQualityScore)} must be between 0 and 100.";
        if (ManualReviewQualityThreshold is < 0 or > 100)
            yield return $"{nameof(ManualReviewQualityThreshold)} must be between 0 and 100.";
        if (ManualReviewQualityThreshold <= MinQualityScore)
            yield return $"{nameof(ManualReviewQualityThreshold)} must be above {nameof(MinQualityScore)}.";
    }
}
