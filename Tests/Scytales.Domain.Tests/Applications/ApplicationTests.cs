using Scytales.Domain.Applications;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;
using static Scytales.Domain.Applications.Workflow.ApplicationStatus;
using static Scytales.Domain.Applications.Workflow.WorkflowEvent;
using static Scytales.Domain.Tests.TestData;

namespace Scytales.Domain.Tests.Applications;

public class ApplicationTests
{
    [Fact]
    public void Submit_starts_in_Submitted_with_a_creation_entry()
    {
        var application = Submit();

        application.Id.Version.ShouldBe(7);
        application.Status.ShouldBe(Submitted);
        application.Version.ShouldBe(1);
        application.CreatedAt.ShouldBe(Now);
        application.UpdatedAt.ShouldBe(Now);
        application.Photo.ShouldNotBeNull();

        var entry = application.History.ShouldHaveSingleItem();
        entry.ApplicationId.ShouldBe(application.Id);
        entry.Version.ShouldBe(1);
        entry.FromStatus.ShouldBeNull();
        entry.ToStatus.ShouldBe(Submitted);
        entry.Event.ShouldBe(ApplicationCreated);
        entry.Details.ShouldBeNull();
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void Timestamps_are_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 6, 9, 13, 30, 0, TimeSpan.FromHours(3));
        var utc = new DateTime(2026, 6, 9, 10, 30, 0);

        var application = Application.Submit(ValidApplicant(), Photo(), local);
        application.StartValidation(local.AddMinutes(1));

        // DateTimeOffset equality compares instants, so pin the offset and the UTC wall-clock explicitly.
        application.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        application.CreatedAt.DateTime.ShouldBe(utc);
        application.UpdatedAt.Offset.ShouldBe(TimeSpan.Zero);
        application.UpdatedAt.DateTime.ShouldBe(utc.AddMinutes(1));
        application.History.ShouldAllBe(e => e.Timestamp.Offset == TimeSpan.Zero);
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void Automatic_approval_flow()
    {
        var application = Submit();

        application.StartValidation(Now.AddMinutes(1));
        application.ValidateData(DataPolicy, Now.AddMinutes(2));
        application.CheckPhoto(GoodScore, PhotoPolicy, Now.AddMinutes(3));
        application.AssessRisk(isWatchlisted: false, RiskPolicy, Now.AddMinutes(4));

        application.Status.ShouldBe(Approved);
        application.Version.ShouldBe(5);
        application.UpdatedAt.ShouldBe(Now.AddMinutes(4));
        application.PhotoQualityScore.ShouldBe(GoodScore);
        application.History.Select(e => e.Event)
            .ShouldBe([ApplicationCreated, ValidationStarted, ValidationPassed, PhotoCheckPassed, AutoApproved]);
        application.History.ShouldAllBe(e => e.Details == null);
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void Manual_review_flow_then_approval()
    {
        var application = InStatus(PendingManualReview);

        application.History[^1].Event.ShouldBe(ManualReviewRequired);
        application.History[^1].Details.ShouldBe("National ID is on the watchlist");

        application.Approve(Now.AddHours(1));

        application.Status.ShouldBe(Approved);
        application.Version.ShouldBe(6);
        application.UpdatedAt.ShouldBe(Now.AddHours(1));
        application.History[^1].FromStatus.ShouldBe(PendingManualReview);
        application.History[^1].Event.ShouldBe(ManuallyApproved);
        application.ShouldHaveConsistentHistory();
    }

    [Theory]
    [InlineData("  Photo does not meet quality requirements ", "Photo does not meet quality requirements")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Reject_records_the_trimmed_reason(string? reason, string? expectedDetails)
    {
        var application = InStatus(PendingManualReview);

        application.Reject(reason, Now.AddHours(1));

        application.Status.ShouldBe(Rejected);
        application.Version.ShouldBe(6);
        application.UpdatedAt.ShouldBe(Now.AddHours(1));
        application.History[^1].Event.ShouldBe(ManuallyRejected);
        application.History[^1].Details.ShouldBe(expectedDetails);
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void Validation_failure_flow()
    {
        var application = Submit(AgedExactly(17));

        application.StartValidation(Now);
        application.ValidateData(DataPolicy, Now);

        application.Status.ShouldBe(Failed);
        application.History.Select(e => e.ToStatus).ShouldBe([Submitted, ValidatingData, Failed]);
        application.History[^1].Event.ShouldBe(ValidationFailed);
        application.History[^1].Details.ShouldBe("Applicant is under 18");
        application.ShouldHaveConsistentHistory();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(500)] // even an out-of-range score: without a photo there is nothing to score
    public void Photo_failure_flow_when_no_photo_was_uploaded_ignores_any_score(int? score)
    {
        var application = Submit(withPhoto: false);

        application.StartValidation(Now);
        application.ValidateData(DataPolicy, Now);
        application.CheckPhoto(score, PhotoPolicy, Now);

        application.Status.ShouldBe(Failed);
        application.PhotoQualityScore.ShouldBeNull();
        application.History[^1].Event.ShouldBe(PhotoCheckFailed);
        application.History[^1].Details.ShouldBe("No photo was provided");
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void A_failed_photo_check_still_records_its_score()
    {
        var application = InStatus(CheckingPhoto);

        application.CheckPhoto(10, PhotoPolicy, Now);

        application.Status.ShouldBe(Failed);
        application.PhotoQualityScore.ShouldBe(10);
        application.History[^1].Details.ShouldBe("Photo quality score 10 is below the minimum of 30");
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void Age_is_judged_on_the_submission_date_not_the_processing_date()
    {
        // 17 on the day of submission, 18 by the time a (delayed or redelivered) message is processed.
        var applicant = ValidApplicant() with { DateOfBirth = Today.AddYears(-18).AddDays(1) };
        var application = Submit(applicant);
        var muchLater = Now.AddDays(7);

        application.StartValidation(muchLater);
        application.ValidateData(DataPolicy, muchLater);

        application.Status.ShouldBe(Failed);
        application.History[^1].Details.ShouldBe("Applicant is under 18");
    }

    public static TheoryData<ApplicationStatus> AllStatusesExceptManualReview() =>
        new(Enum.GetValues<ApplicationStatus>().Where(s => s != PendingManualReview));

    [Theory]
    [MemberData(nameof(AllStatusesExceptManualReview))]
    public void Approve_outside_manual_review_is_rejected_and_changes_nothing(ApplicationStatus status)
    {
        var application = InStatus(status);
        var version = application.Version;
        var updatedAt = application.UpdatedAt;
        var entries = application.History.Count;

        var ex = Should.Throw<InvalidTransitionException>(() => application.Approve(Now.AddHours(1)));

        ex.ApplicationId.ShouldBe(application.Id);
        ex.CurrentStatus.ShouldBe(status);
        ex.AttemptedEvent.ShouldBe(ManuallyApproved);
        application.Status.ShouldBe(status);
        application.Version.ShouldBe(version);
        application.UpdatedAt.ShouldBe(updatedAt);
        application.History.Count.ShouldBe(entries);
    }

    [Theory]
    [MemberData(nameof(AllStatusesExceptManualReview))]
    public void Reject_outside_manual_review_is_rejected(ApplicationStatus status)
    {
        var application = InStatus(status);

        Should.Throw<InvalidTransitionException>(() => application.Reject("no", Now))
            .AttemptedEvent.ShouldBe(ManuallyRejected);
        application.Status.ShouldBe(status);
    }

    [Fact]
    public void Steps_cannot_be_skipped()
    {
        var application = Submit();

        Should.Throw<InvalidTransitionException>(() => application.ValidateData(DataPolicy, Now));
        Should.Throw<InvalidTransitionException>(() => application.CheckPhoto(GoodScore, PhotoPolicy, Now));
        Should.Throw<InvalidTransitionException>(() => application.AssessRisk(false, RiskPolicy, Now));
        application.Status.ShouldBe(Submitted);
    }

    [Theory]
    [InlineData(Submitted)]
    [InlineData(ValidatingData)]
    [InlineData(CheckingPhoto)]
    [InlineData(RiskAssessment)]
    public void Fail_ends_any_automated_step(ApplicationStatus status)
    {
        var application = InStatus(status);
        var version = application.Version;

        application.Fail("  Retries exhausted: database unavailable ", Now.AddHours(1));

        application.Status.ShouldBe(Failed);
        application.Version.ShouldBe(version + 1);
        application.UpdatedAt.ShouldBe(Now.AddHours(1));
        application.History[^1].FromStatus.ShouldBe(status);
        application.History[^1].Event.ShouldBe(ProcessingFailed);
        application.History[^1].Details.ShouldBe("Retries exhausted: database unavailable");
        application.ShouldHaveConsistentHistory();
    }

    [Theory]
    [InlineData(PendingManualReview)]
    [InlineData(Approved)]
    [InlineData(Rejected)]
    [InlineData(Failed)]
    public void Fail_cannot_override_manual_review_or_a_final_decision(ApplicationStatus status)
    {
        var application = InStatus(status);

        Should.Throw<InvalidTransitionException>(() => application.Fail("late fault", Now));
        application.Status.ShouldBe(status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fail_requires_a_reason(string reason)
    {
        var application = Submit();

        Should.Throw<ArgumentException>(() => application.Fail(reason, Now));
        application.Status.ShouldBe(Submitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(101)]
    public void A_photo_needs_a_score_between_0_and_100(int? score)
    {
        var application = InStatus(CheckingPhoto);

        Should.Throw<ArgumentOutOfRangeException>(() => application.CheckPhoto(score, PhotoPolicy, Now));
        application.Status.ShouldBe(CheckingPhoto);
    }

    [Fact]
    public void A_wrong_state_is_reported_before_a_bad_score()
    {
        var application = Submit();

        Should.Throw<InvalidTransitionException>(() => application.CheckPhoto(500, PhotoPolicy, Now))
            .AttemptedEvent.ShouldBe(PhotoCheckPassed);
    }

    [Fact]
    public void Low_photo_quality_is_carried_into_risk_assessment()
    {
        var application = InStatus(CheckingPhoto);

        application.CheckPhoto(45, PhotoPolicy, Now);
        application.AssessRisk(isWatchlisted: false, RiskPolicy, Now);

        application.Status.ShouldBe(PendingManualReview);
        application.History[^1].Details.ShouldBe("Photo quality score 45 is below the review threshold of 60");
        application.ShouldHaveConsistentHistory();
    }

    [Fact]
    public void Policies_are_replaceable()
    {
        var application = InStatus(ValidatingData);

        application.ValidateData(new AlwaysFails("Rejected by a custom policy"), Now);

        application.Status.ShouldBe(Failed);
        application.History[^1].Details.ShouldBe("Rejected by a custom policy");
    }

    [Fact]
    public void Required_arguments_are_guarded()
    {
        Should.Throw<ArgumentNullException>(() => Application.Submit(null!, Photo(), Now));
        Should.Throw<ArgumentNullException>(() => InStatus(ValidatingData).ValidateData(null!, Now));
        Should.Throw<ArgumentNullException>(() => InStatus(CheckingPhoto).CheckPhoto(GoodScore, null!, Now));
        Should.Throw<ArgumentNullException>(() => InStatus(RiskAssessment).AssessRisk(false, null!, Now));
    }

    private sealed class AlwaysFails(string reason) : IDataValidationPolicy
    {
        public RuleResult Evaluate(ApplicantDetails applicant, DateTimeOffset submittedAt) => new([reason]);
    }
}
