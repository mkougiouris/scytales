using Scytales.Domain.Applications.Workflow;
using static Scytales.Application.Tests.TestData;
using static Scytales.Domain.Applications.Workflow.ApplicationStatus;

namespace Scytales.Application.Tests.UseCases.Fail;

public class FailApplicationHandlerTests
{
    private const string Reason = "Retries exhausted: photo service unavailable";

    private readonly Harness _harness = new();

    private Task Fail(Guid applicationId) => _harness.Fail().HandleAsync(applicationId, Reason, CancellationToken.None);

    [Theory]
    [InlineData(Submitted)]
    [InlineData(ValidatingData)]
    [InlineData(CheckingPhoto)]
    [InlineData(ApplicationStatus.RiskAssessment)]
    public async Task Fails_an_application_stuck_in_an_automated_step(ApplicationStatus status)
    {
        var application = _harness.Store.Seed(InStatus(status));

        await Fail(application.Id);

        application.Status.ShouldBe(Failed);
        var entry = application.History[^1];
        entry.FromStatus.ShouldBe(status);
        entry.Event.ShouldBe(WorkflowEvent.ProcessingFailed);
        entry.Details.ShouldBe(Reason);
        entry.Timestamp.ShouldBe(_harness.Clock.GetUtcNow());
        _harness.Store.Saves.ShouldBe(1);
    }

    [Theory]
    [InlineData(PendingManualReview)]
    [InlineData(Approved)]
    [InlineData(Rejected)]
    [InlineData(Failed)]
    public async Task Leaves_an_application_that_has_moved_on_untouched(ApplicationStatus status)
    {
        var application = _harness.Store.Seed(InStatus(status));
        var version = application.Version;

        await Fail(application.Id);

        application.Status.ShouldBe(status);
        application.Version.ShouldBe(version);
        _harness.Store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_application_is_ignored()
    {
        await Should.NotThrowAsync(() => Fail(Guid.NewGuid()));

        _harness.Store.Saves.ShouldBe(0);
    }
}
