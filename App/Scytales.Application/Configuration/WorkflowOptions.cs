using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Application.Configuration;

/// <summary>
/// The configuration shape of <see cref="WorkflowRules"/> (section <c>WorkflowRules</c>).
/// Every value is optional and falls back to the Domain default. Collections start out null on purpose:
/// the configuration binder appends to pre-filled collections, so defaults there could never be narrowed.
/// </summary>
public sealed class WorkflowOptions
{
    public const string SectionName = "WorkflowRules";

    public int? MinimumAge { get; set; }
    public int? ManualReviewBelowAge { get; set; }
    public List<LicenceCategory>? AutoApprovedCategories { get; set; }
    public string? TimeZoneId { get; set; }
    public PhotoOptions Photo { get; set; } = new();

    public sealed class PhotoOptions
    {
        public List<PhotoFormat>? AllowedFormats { get; set; }
        public long? MinFileSizeBytes { get; set; }
        public int? MinQualityScore { get; set; }
        public int? ManualReviewQualityThreshold { get; set; }
    }

    public WorkflowRules ToRules()
    {
        var defaults = new WorkflowRules();

        return new WorkflowRules
        {
            MinimumAge = MinimumAge ?? defaults.MinimumAge,
            ManualReviewBelowAge = ManualReviewBelowAge ?? defaults.ManualReviewBelowAge,
            AutoApprovedCategories = AutoApprovedCategories?.ToArray() ?? defaults.AutoApprovedCategories,
            TimeZoneId = TimeZoneId ?? defaults.TimeZoneId,
            Photo = new PhotoRules
            {
                AllowedFormats = Photo.AllowedFormats?.ToArray() ?? defaults.Photo.AllowedFormats,
                MinFileSizeBytes = Photo.MinFileSizeBytes ?? defaults.Photo.MinFileSizeBytes,
                MinQualityScore = Photo.MinQualityScore ?? defaults.Photo.MinQualityScore,
                ManualReviewQualityThreshold = Photo.ManualReviewQualityThreshold ?? defaults.Photo.ManualReviewQualityThreshold
            }
        };
    }
}
