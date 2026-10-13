using Microsoft.Extensions.Configuration;
using Scytales.Application.Configuration;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Application.Tests.Configuration;

public class WorkflowOptionsTests
{
    [Fact]
    public void Empty_options_fall_back_to_the_domain_defaults()
    {
        var rules = new WorkflowOptions().ToRules();
        var defaults = new WorkflowRules();

        rules.MinimumAge.ShouldBe(defaults.MinimumAge);
        rules.ManualReviewBelowAge.ShouldBe(defaults.ManualReviewBelowAge);
        rules.AutoApprovedCategories.ShouldBe(defaults.AutoApprovedCategories);
        rules.TimeZoneId.ShouldBe(defaults.TimeZoneId);
        rules.Photo.AllowedFormats.ShouldBe(defaults.Photo.AllowedFormats);
        rules.Photo.MinFileSizeBytes.ShouldBe(defaults.Photo.MinFileSizeBytes);
        rules.Photo.MinQualityScore.ShouldBe(defaults.Photo.MinQualityScore);
        rules.Photo.ManualReviewQualityThreshold.ShouldBe(defaults.Photo.ManualReviewQualityThreshold);
    }

    [Fact]
    public void Configured_values_override_the_defaults()
    {
        var options = new WorkflowOptions
        {
            MinimumAge = 17,
            ManualReviewBelowAge = 25,
            AutoApprovedCategories = [LicenceCategory.B, LicenceCategory.BE],
            TimeZoneId = "UTC",
            Photo = new WorkflowOptions.PhotoOptions
            {
                AllowedFormats = [PhotoFormat.Png],
                MinFileSizeBytes = 2048,
                MinQualityScore = 40,
                ManualReviewQualityThreshold = 70
            }
        };

        var rules = options.ToRules();

        rules.MinimumAge.ShouldBe(17);
        rules.ManualReviewBelowAge.ShouldBe(25);
        rules.AutoApprovedCategories.ShouldBe([LicenceCategory.B, LicenceCategory.BE]);
        rules.TimeZoneId.ShouldBe("UTC");
        rules.Photo.AllowedFormats.ShouldBe([PhotoFormat.Png]);
        rules.Photo.MinFileSizeBytes.ShouldBe(2048);
        rules.Photo.MinQualityScore.ShouldBe(40);
        rules.Photo.ManualReviewQualityThreshold.ShouldBe(70);
    }

    [Fact]
    public void Binding_from_configuration_narrows_collections_instead_of_appending()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowRules:Photo:AllowedFormats:0"] = "Png",
                ["WorkflowRules:AutoApprovedCategories:0"] = "BE"
            })
            .Build();

        var rules = configuration.GetSection(WorkflowOptions.SectionName).Get<WorkflowOptions>()!.ToRules();

        rules.Photo.AllowedFormats.ShouldBe([PhotoFormat.Png]);
        rules.AutoApprovedCategories.ShouldBe([LicenceCategory.BE]);
    }

    [Fact]
    public void Binding_reads_values_case_insensitively_and_keeps_unset_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowRules:Photo:AllowedFormats:0"] = "jpeg",
                ["WorkflowRules:Photo:MinQualityScore"] = "20"
            })
            .Build();

        var rules = configuration.GetSection(WorkflowOptions.SectionName).Get<WorkflowOptions>()!.ToRules();

        rules.Photo.AllowedFormats.ShouldBe([PhotoFormat.Jpeg]);
        rules.Photo.MinQualityScore.ShouldBe(20);
        rules.Photo.ManualReviewQualityThreshold.ShouldBe(new WorkflowRules().Photo.ManualReviewQualityThreshold);
    }
}
