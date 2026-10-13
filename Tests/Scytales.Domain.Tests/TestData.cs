using Scytales.Domain.Applications;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Domain.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 6, 9, 10, 30, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    public static readonly WorkflowRules Rules = new();
    public static readonly IDataValidationPolicy DataPolicy = new DataValidationPolicy(Rules);
    public static readonly IPhotoCheckPolicy PhotoPolicy = new PhotoCheckPolicy(Rules);
    public static readonly IRiskAssessmentPolicy RiskPolicy = new RiskAssessmentPolicy(Rules);

    public const int GoodScore = 90;

    public static ApplicantDetails ValidApplicant() => new(
        FirstName: "John",
        LastName: "Doe",
        DateOfBirth: new DateOnly(1990, 5, 20),
        NationalId: "AB123456",
        Email: "john.doe@example.com",
        PhoneNumber: "+306900000000",
        Address: "Example Street 10",
        LicenceCategory: "B");

    /// <summary>An applicant who turns <paramref name="years"/> exactly on <see cref="Today"/>.</summary>
    public static ApplicantDetails AgedExactly(int years) => ValidApplicant() with { DateOfBirth = Today.AddYears(-years) };

    public static byte[] Jpeg(int size = 20_000) => WithSignature(size, 0xFF, 0xD8, 0xFF);
    public static byte[] Png(int size = 20_000) => WithSignature(size, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    public static byte[] Gif(int size = 20_000) => WithSignature(size, 0x47, 0x49, 0x46, 0x38, 0x39, 0x61);

    public static ApplicationPhoto Photo(byte[]? content = null, string contentType = "image/jpeg") =>
        new("face.jpg", contentType, content ?? Jpeg());

    public static Application Submit(ApplicantDetails? applicant = null, bool withPhoto = true) =>
        Application.Submit(applicant ?? ValidApplicant(), withPhoto ? Photo() : null, Now);

    /// <summary>Drives a valid application through the real workflow until it reaches <paramref name="status"/>.</summary>
    public static Application InStatus(ApplicationStatus status)
    {
        var application = Drive(status);

        application.Status.ShouldBe(status);
        application.ShouldHaveConsistentHistory();
        return application;
    }

    private static Application Drive(ApplicationStatus status)
    {
        var application = Submit();
        if (status == ApplicationStatus.Submitted)
            return application;

        if (status == ApplicationStatus.Failed)
        {
            application.Fail("Simulated technical failure", Now);
            return application;
        }

        application.StartValidation(Now);
        if (status == ApplicationStatus.ValidatingData)
            return application;

        application.ValidateData(DataPolicy, Now);
        if (status == ApplicationStatus.CheckingPhoto)
            return application;

        application.CheckPhoto(GoodScore, PhotoPolicy, Now);
        if (status == ApplicationStatus.RiskAssessment)
            return application;

        var needsReview = status is ApplicationStatus.PendingManualReview or ApplicationStatus.Rejected;
        application.AssessRisk(isWatchlisted: needsReview, RiskPolicy, Now);

        if (status == ApplicationStatus.Rejected)
            application.Reject("Not convinced", Now);

        return application;
    }

    /// <summary>
    /// The audit trail is an unbroken chain: each entry starts where the previous one ended, versions step by one,
    /// time never goes backwards, and the last entry matches the application's current state.
    /// </summary>
    public static void ShouldHaveConsistentHistory(this Application application)
    {
        var history = application.History;

        history.ShouldNotBeEmpty();
        history[0].FromStatus.ShouldBeNull();
        history[0].Event.ShouldBe(WorkflowEvent.ApplicationCreated);
        history.ShouldAllBe(e => e.ApplicationId == application.Id);

        for (var i = 1; i < history.Count; i++)
        {
            history[i].FromStatus.ShouldBe(history[i - 1].ToStatus);
            history[i].Version.ShouldBe(history[i - 1].Version + 1);
            history[i].Timestamp.ShouldBeGreaterThanOrEqualTo(history[i - 1].Timestamp);
        }

        history[^1].ToStatus.ShouldBe(application.Status);
        history[^1].Version.ShouldBe(application.Version);
        history[^1].Timestamp.ShouldBe(application.UpdatedAt);
    }

    private static byte[] WithSignature(int size, params byte[] signature)
    {
        var content = new byte[Math.Max(size, signature.Length)];
        signature.CopyTo(content, 0);
        return content;
    }
}
