using static Scytales.Domain.Applications.Workflow.ApplicationStatus;
using static Scytales.Domain.Applications.Workflow.WorkflowEvent;

namespace Scytales.Domain.Applications.Workflow;

/// <summary>
/// The single source of truth for allowed transitions: (current status, event) → next status.
/// Any pair not listed here is an invalid transition.
/// </summary>
public static class ApplicationWorkflow
{
    private static readonly Dictionary<(ApplicationStatus From, WorkflowEvent Event), ApplicationStatus> Transitions = new()
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

        // Technical failures (retries exhausted) can end any automated step.
        [(Submitted, ProcessingFailed)] = Failed,
        [(ValidatingData, ProcessingFailed)] = Failed,
        [(CheckingPhoto, ProcessingFailed)] = Failed,
        [(RiskAssessment, ProcessingFailed)] = Failed,
    };

    public static bool TryGetTarget(ApplicationStatus from, WorkflowEvent @event, out ApplicationStatus to) =>
        Transitions.TryGetValue((from, @event), out to);
}
