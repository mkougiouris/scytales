using Scytales.Application.Exceptions;
using Scytales.Domain.Applications.Workflow;
using static Scytales.Application.Tests.TestData;
using static Scytales.Domain.Applications.Workflow.ApplicationStatus;
using static Scytales.Domain.Applications.Workflow.WorkflowEvent;

namespace Scytales.Application.Tests.UseCases.Process;

public class ProcessApplicationHandlerTests
{
    private Harness _harness = new();

    private Task Process(Guid applicationId) => _harness.Process().HandleAsync(applicationId, CancellationToken.None);

    [Fact]
    public async Task A_clean_application_is_approved_automatically()
    {
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        application.Status.ShouldBe(Approved);
        application.PhotoQualityScore.ShouldBe(GoodScore);
        application.History.Select(e => e.Event)
            .ShouldBe([ApplicationCreated, ValidationStarted, ValidationPassed, PhotoCheckPassed, AutoApproved]);
    }

    [Fact]
    public async Task Saves_once_per_step()
    {
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        _harness.Store.Saves.ShouldBe(4);
        _harness.Store.Calls.ShouldBe(["Get", "Save", "Save", "Save", "Save"]);
    }

    [Fact]
    public async Task A_watchlisted_applicant_goes_to_manual_review()
    {
        _harness = new Harness(NationalId);
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        application.Status.ShouldBe(PendingManualReview);
        application.History[^1].Details.ShouldBe("National ID is on the watchlist");
    }

    [Fact]
    public async Task The_watchlist_is_asked_about_the_applicants_national_id()
    {
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        _harness.Watchlist.Queried.ShouldBe([NationalId]);
    }

    [Fact]
    public async Task The_scorer_receives_the_applications_photo()
    {
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        _harness.Scorer.Scored.ShouldHaveSingleItem().ShouldBe(application.Photo);
    }

    [Fact]
    public async Task Validation_failure_stops_before_the_photo_and_risk_steps()
    {
        var application = _harness.Store.Seed(NewApplication(ValidApplicant() with { DateOfBirth = Today.AddYears(-16) }));

        await Process(application.Id);

        application.Status.ShouldBe(Failed);
        application.History[^1].Event.ShouldBe(ValidationFailed);
        _harness.Scorer.Scored.ShouldBeEmpty();
        _harness.Watchlist.Queried.ShouldBeEmpty();
        _harness.Store.Saves.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_photo_the_scorer_is_not_called_and_the_application_fails()
    {
        var application = _harness.Store.Seed(NewApplication(withPhoto: false));

        await Process(application.Id);

        application.Status.ShouldBe(Failed);
        application.History[^1].Details.ShouldBe("No photo was provided");
        _harness.Scorer.Scored.ShouldBeEmpty();
        _harness.Watchlist.Queried.ShouldBeEmpty();
        _harness.Store.Saves.ShouldBe(3);
    }

    [Fact]
    public async Task A_score_below_the_review_threshold_goes_to_manual_review()
    {
        _harness.Scorer.Score = 45;
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        application.Status.ShouldBe(PendingManualReview);
        application.PhotoQualityScore.ShouldBe(45);
    }

    [Fact]
    public async Task Resumes_from_the_last_saved_step()
    {
        // As if a previous attempt crashed after saving CheckingPhoto.
        var application = _harness.Store.Seed(InStatus(CheckingPhoto));

        await Process(application.Id);

        application.Status.ShouldBe(Approved);
        application.History.Select(e => e.Event).TakeLast(2).ShouldBe([PhotoCheckPassed, AutoApproved]);
        _harness.Store.Saves.ShouldBe(2);
    }

    [Theory]
    [InlineData(PendingManualReview)]
    [InlineData(Approved)]
    [InlineData(Rejected)]
    [InlineData(Failed)]
    public async Task A_redelivered_message_for_a_settled_application_does_nothing(ApplicationStatus status)
    {
        var application = _harness.Store.Seed(InStatus(status));
        var version = application.Version;

        await Process(application.Id);

        application.Status.ShouldBe(status);
        application.Version.ShouldBe(version);
        _harness.Store.Saves.ShouldBe(0);
        _harness.Scorer.Scored.ShouldBeEmpty();
        _harness.Watchlist.Queried.ShouldBeEmpty();
    }

    [Fact]
    public async Task Processing_twice_is_the_same_as_processing_once()
    {
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);
        await Process(application.Id);

        application.Status.ShouldBe(Approved);
        application.Version.ShouldBe(5);
        _harness.Store.Saves.ShouldBe(4);
    }

    [Fact]
    public async Task An_unknown_application_throws()
    {
        var id = Guid.NewGuid();

        var ex = await Should.ThrowAsync<ApplicationNotFoundException>(() => Process(id));

        ex.ApplicationId.ShouldBe(id);
        _harness.Store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_concurrency_conflict_on_save_propagates_so_the_message_is_retried()
    {
        var application = _harness.Store.Seed(NewApplication());
        _harness.Store.ConflictOnSave = true;

        await Should.ThrowAsync<ConcurrencyConflictException>(() => Process(application.Id));

        _harness.Store.Calls.Count(c => c == "Save").ShouldBe(1);
    }

    [Fact]
    public async Task Each_step_is_stamped_with_the_clock_at_that_moment()
    {
        _harness.Clock.AutoAdvanceAmount = TimeSpan.FromSeconds(1);
        var application = _harness.Store.Seed(NewApplication());

        await Process(application.Id);

        var stepTimestamps = application.History.Skip(1).Select(e => e.Timestamp).ToList();
        stepTimestamps.ShouldBeInOrder(SortDirection.Ascending);
        stepTimestamps.Distinct().Count().ShouldBe(4);
        application.UpdatedAt.ShouldBe(stepTimestamps[^1]);
    }
}
