using System.Collections.Concurrent;
using System.Diagnostics;
using static Scytales.Application.Tests.TestData;

namespace Scytales.Application.Tests.UseCases.Process;

/// <summary>D-18: one span per automated step, tagged with where it started and ended.</summary>
public sealed class WorkflowTelemetryTests : IDisposable
{
    private readonly Harness _harness = new();
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public WorkflowTelemetryTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // Other tests run in parallel and record spans too: keep only this application's.
    private Activity[] SpansFor(Guid applicationId) =>
        _stopped.Where(a => Equals(a.GetTagItem("application.id"), applicationId)).ToArray();

    [Fact]
    public async Task Records_one_span_per_automated_step_in_order()
    {
        var application = _harness.Store.Seed(NewApplication());

        await _harness.Process().HandleAsync(application.Id, CancellationToken.None);

        var spans = SpansFor(application.Id);
        spans.Select(s => s.DisplayName).ShouldBe(
            ["workflow Submitted", "workflow ValidatingData", "workflow CheckingPhoto", "workflow RiskAssessment"]);
        spans.Select(s => s.GetTagItem("workflow.event")).ShouldBe(
            ["ValidationStarted", "ValidationPassed", "PhotoCheckPassed", "AutoApproved"]);
        spans[^1].GetTagItem("workflow.to_status").ShouldBe("Approved");
    }

    [Fact]
    public async Task Tags_the_reason_when_a_step_has_one()
    {
        var application = _harness.Store.Seed(NewApplication(ValidApplicant() with { Email = "not-an-email" }));

        await _harness.Process().HandleAsync(application.Id, CancellationToken.None);

        var failed = SpansFor(application.Id)[^1];
        failed.GetTagItem("workflow.event").ShouldBe("ValidationFailed");
        failed.GetTagItem("workflow.details").ShouldBe("Email is invalid");
    }

    [Fact]
    public async Task Records_nothing_when_no_step_applies()
    {
        var application = _harness.Store.Seed(InStatus(Domain.Applications.Workflow.ApplicationStatus.Approved));

        await _harness.Process().HandleAsync(application.Id, CancellationToken.None);

        SpansFor(application.Id).ShouldBeEmpty();
    }
}
