using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Domain.Applications.Policies;

public sealed class PhotoCheckPolicy(WorkflowRules rules) : IPhotoCheckPolicy
{
    public RuleResult Evaluate(ApplicationPhoto? photo, int? qualityScore)
    {
        if (photo is null)
            return new RuleResult(["No photo was provided"]);

        var photoRules = rules.Photo;
        List<string> reasons = [];

        if (photo.DetectFormat() is not { } format || !photoRules.AllowedFormats.Contains(format))
            reasons.Add($"File type is unsupported (declared '{photo.ContentType}')");

        if (photo.FileSize < photoRules.MinFileSizeBytes)
            reasons.Add($"File size {photo.FileSize} bytes is below the minimum of {photoRules.MinFileSizeBytes} bytes");

        if (qualityScore is null)
            reasons.Add("Photo quality score is missing");
        else if (qualityScore < photoRules.MinQualityScore)
            reasons.Add($"Photo quality score {qualityScore} is below the minimum of {photoRules.MinQualityScore}");

        return new RuleResult(reasons);
    }
}
