using Scytales.Domain.Applications.Workflow;
using static Scytales.Domain.Applications.Workflow.ApplicationStatus;
using static Scytales.Domain.Applications.Workflow.WorkflowEvent;

namespace Scytales.Domain.Tests.Applications.Workflow;

public class ApplicationWorkflowTests
{
    // The specification, written out independently of the production table.
    private static readonly Dictionary<(ApplicationStatus, WorkflowEvent), ApplicationStatus> Expected = new()
    {
        [(Submitted, ValidationStarted)] = ValidatingData,
        [(ValidatingData, ValidationPassed)] = CheckingPhoto,
        [(ValidatingData, ValidationFailed)] = Failed,
        [(CheckingPhoto, PhotoCheckPassed)] = RiskAssessment,
        [(CheckingPhoto, PhotoCheckFailed)] = Failed,
        [(RiskAssessment, AutoApproved)] = Approved,
        [(RiskAssessment, ManualReviewRequired)] = PendingManualReview,
        [(PendingManualReview, ManuallyApproved)] = Approved,
        [(PendingManualReview, ManuallyRejected)] = Rejected,
        [(Submitted, ProcessingFailed)] = Failed,
        [(ValidatingData, ProcessingFailed)] = Failed,
        [(CheckingPhoto, ProcessingFailed)] = Failed,
        [(RiskAssessment, ProcessingFailed)] = Failed,
    };

    public static TheoryData<ApplicationStatus, WorkflowEvent> AllPairs()
    {
        var data = new TheoryData<ApplicationStatus, WorkflowEvent>();
        foreach (var status in Enum.GetValues<ApplicationStatus>())
            foreach (var @event in Enum.GetValues<WorkflowEvent>())
                data.Add(status, @event);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Only_specified_transitions_are_allowed(ApplicationStatus from, WorkflowEvent @event)
    {
        var allowed = ApplicationWorkflow.TryGetTarget(from, @event, out var target);

        if (Expected.TryGetValue((from, @event), out var expectedTarget))
        {
            allowed.ShouldBeTrue();
            target.ShouldBe(expectedTarget);
        }
        else
        {
            allowed.ShouldBeFalse();
        }
    }
}
