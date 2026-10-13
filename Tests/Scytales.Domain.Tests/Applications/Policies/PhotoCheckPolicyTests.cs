using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications.Policies;

public class PhotoCheckPolicyTests
{
    private const int MinSize = 10 * 1024;
    private const int MinScore = 30;

    [Fact]
    public void No_photo_fails()
    {
        PhotoPolicy.Evaluate(null, null).Reasons.ShouldBe(["No photo was provided"]);
    }

    [Fact]
    public void A_good_jpeg_passes()
    {
        PhotoPolicy.Evaluate(Photo(Jpeg()), GoodScore).Passed.ShouldBeTrue();
    }

    [Fact]
    public void A_good_png_passes()
    {
        PhotoPolicy.Evaluate(Photo(Png(), "image/png"), GoodScore).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Unsupported_format_fails()
    {
        PhotoPolicy.Evaluate(Photo(Gif(), "image/gif"), GoodScore)
            .Reasons.ShouldBe(["File type is unsupported (declared 'image/gif')"]);
    }

    [Fact]
    public void Format_is_judged_by_content_not_by_the_declared_type()
    {
        PhotoPolicy.Evaluate(Photo(Gif(), "image/jpeg"), GoodScore).Passed.ShouldBeFalse();
        PhotoPolicy.Evaluate(Photo(Png(), "application/octet-stream"), GoodScore).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Allowed_formats_are_configurable()
    {
        var pngOnly = new PhotoCheckPolicy(new WorkflowRules { Photo = new PhotoRules { AllowedFormats = [PhotoFormat.Png] } });

        pngOnly.Evaluate(Photo(Jpeg()), GoodScore).Passed.ShouldBeFalse();
        pngOnly.Evaluate(Photo(Png()), GoodScore).Passed.ShouldBeTrue();
    }

    [Fact]
    public void File_at_the_minimum_size_passes()
    {
        PhotoPolicy.Evaluate(Photo(Jpeg(MinSize)), GoodScore).Passed.ShouldBeTrue();
    }

    [Fact]
    public void File_below_the_minimum_size_fails()
    {
        PhotoPolicy.Evaluate(Photo(Jpeg(MinSize - 1)), GoodScore)
            .Reasons.ShouldBe([$"File size {MinSize - 1} bytes is below the minimum of {MinSize} bytes"]);
    }

    [Fact]
    public void Score_at_the_minimum_passes()
    {
        PhotoPolicy.Evaluate(Photo(), MinScore).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Score_below_the_minimum_fails()
    {
        PhotoPolicy.Evaluate(Photo(), MinScore - 1)
            .Reasons.ShouldBe([$"Photo quality score {MinScore - 1} is below the minimum of {MinScore}"]);
    }

    [Fact]
    public void A_photo_without_a_score_fails_rather_than_skipping_the_quality_rule()
    {
        PhotoPolicy.Evaluate(Photo(), null).Reasons.ShouldBe(["Photo quality score is missing"]);
    }

    [Fact]
    public void All_reasons_are_reported_together()
    {
        PhotoPolicy.Evaluate(Photo(Gif(100), "image/gif"), 5).Reasons.ShouldBe(
        [
            "File type is unsupported (declared 'image/gif')",
            $"File size 100 bytes is below the minimum of {MinSize} bytes",
            $"Photo quality score 5 is below the minimum of {MinScore}"
        ]);
    }
}
