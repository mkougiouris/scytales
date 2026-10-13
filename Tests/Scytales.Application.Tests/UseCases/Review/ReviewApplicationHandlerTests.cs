using Scytales.Application.Exceptions;
using Scytales.Domain.Applications.Workflow;
using static Scytales.Application.Tests.TestData;
using static Scytales.Domain.Applications.Workflow.ApplicationStatus;
using static Scytales.Domain.Applications.Workflow.WorkflowEvent;

namespace Scytales.Application.Tests.UseCases.Review;

public class ReviewApplicationHandlerTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task Approve_from_manual_review_returns_the_new_status_and_saves_once()
    {
        var application = _harness.Store.Seed(InStatus(PendingManualReview));

        var dto = await _harness.Review().ApproveAsync(application.Id, CancellationToken.None);

        dto.ApplicationId.ShouldBe(application.Id);
        dto.Status.ShouldBe(Approved);
        dto.CreatedAt.ShouldBe(application.CreatedAt);
        dto.UpdatedAt.ShouldBe(_harness.Clock.GetUtcNow());
        application.History[^1].Event.ShouldBe(ManuallyApproved);
        _harness.Store.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Reject_from_manual_review_records_the_reason()
    {
        var application = _harness.Store.Seed(InStatus(PendingManualReview));

        var dto = await _harness.Review().RejectAsync(application.Id, "Photo does not meet quality requirements", CancellationToken.None);

        dto.Status.ShouldBe(Rejected);
        application.History[^1].Event.ShouldBe(ManuallyRejected);
        application.History[^1].Details.ShouldBe("Photo does not meet quality requirements");
        application.History[^1].Timestamp.ShouldBe(_harness.Clock.GetUtcNow());
        _harness.Store.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Reject_without_a_reason_is_allowed()
    {
        var application = _harness.Store.Seed(InStatus(PendingManualReview));

        var dto = await _harness.Review().RejectAsync(application.Id, null, CancellationToken.None);

        dto.Status.ShouldBe(Rejected);
        application.History[^1].Details.ShouldBeNull();
    }

    [Fact]
    public async Task Approving_an_unknown_application_throws_not_found()
    {
        var id = Guid.NewGuid();

        (await Should.ThrowAsync<ApplicationNotFoundException>(() => _harness.Review().ApproveAsync(id, CancellationToken.None)))
            .ApplicationId.ShouldBe(id);
        _harness.Store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Rejecting_an_unknown_application_throws_not_found()
    {
        await Should.ThrowAsync<ApplicationNotFoundException>(() => _harness.Review().RejectAsync(Guid.NewGuid(), "no", CancellationToken.None));
        _harness.Store.Saves.ShouldBe(0);
    }

    // Which statuses refuse is the Domain's rule (ApplicationTests); here, one in-flight and one final status
    // show the handler passes the refusal on and saves nothing.
    [Theory]
    [InlineData(CheckingPhoto)]
    [InlineData(Approved)]
    public async Task Approving_outside_manual_review_is_an_invalid_transition_and_saves_nothing(ApplicationStatus status)
    {
        var application = _harness.Store.Seed(InStatus(status));

        var ex = await Should.ThrowAsync<InvalidTransitionException>(() => _harness.Review().ApproveAsync(application.Id, CancellationToken.None));

        ex.CurrentStatus.ShouldBe(status);
        ex.AttemptedEvent.ShouldBe(ManuallyApproved);
        application.Status.ShouldBe(status);
        _harness.Store.Saves.ShouldBe(0);
    }

    [Theory]
    [InlineData(Submitted)]
    [InlineData(Failed)]
    public async Task Rejecting_outside_manual_review_is_an_invalid_transition_and_saves_nothing(ApplicationStatus status)
    {
        var application = _harness.Store.Seed(InStatus(status));

        (await Should.ThrowAsync<InvalidTransitionException>(() => _harness.Review().RejectAsync(application.Id, "no", CancellationToken.None)))
            .AttemptedEvent.ShouldBe(ManuallyRejected);
        _harness.Store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_concurrency_conflict_on_save_propagates()
    {
        var application = _harness.Store.Seed(InStatus(PendingManualReview));
        _harness.Store.ConflictOnSave = true;

        await Should.ThrowAsync<ConcurrencyConflictException>(() => _harness.Review().ApproveAsync(application.Id, CancellationToken.None));
    }
}
