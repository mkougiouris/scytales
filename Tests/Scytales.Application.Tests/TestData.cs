using Scytales.Application.UseCases.Submit;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 6, 9, 10, 30, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    public static readonly WorkflowRules Rules = new();
    public static readonly IDataValidationPolicy DataPolicy = new DataValidationPolicy(Rules);
    public static readonly IPhotoCheckPolicy PhotoPolicy = new PhotoCheckPolicy(Rules);
    public static readonly IRiskAssessmentPolicy RiskPolicy = new RiskAssessmentPolicy(Rules);

    public const int GoodScore = 90;
    public const string NationalId = "AB123456";

    public static ApplicantDetails ValidApplicant() => new(
        FirstName: "John",
        LastName: "Doe",
        DateOfBirth: new DateOnly(1990, 5, 20),
        NationalId: NationalId,
        Email: "john.doe@example.com",
        PhoneNumber: "+306900000000",
        Address: "Example Street 10",
        LicenceCategory: "B");

    public static byte[] Jpeg(int size = 20_000)
    {
        var content = new byte[size];
        content[0] = 0xFF;
        content[1] = 0xD8;
        content[2] = 0xFF;
        return content;
    }

    public static PhotoUpload Upload() => new("face.jpg", "image/jpeg", Jpeg());

    public static SubmitApplicationCommand ValidCommand(PhotoUpload? photo = null) => new(
        FirstName: "John",
        LastName: "Doe",
        DateOfBirth: new DateOnly(1990, 5, 20),
        NationalId: NationalId,
        Email: "john.doe@example.com",
        PhoneNumber: "+306900000000",
        Address: "Example Street 10",
        LicenceCategory: "B",
        Photo: photo ?? Upload());

    public static LicenceApplication NewApplication(ApplicantDetails? applicant = null, bool withPhoto = true) =>
        LicenceApplication.Submit(applicant ?? ValidApplicant(), withPhoto ? new ApplicationPhoto("face.jpg", "image/jpeg", Jpeg()) : null, Now);

    /// <summary>Drives a valid application through the real Domain workflow until it reaches <paramref name="status"/>.</summary>
    public static LicenceApplication InStatus(ApplicationStatus status)
    {
        var application = NewApplication();
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

        application.Status.ShouldBe(status);
        return application;
    }
}
