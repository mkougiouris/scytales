namespace Scytales.Domain.Applications.Workflow;

/// <summary>
/// The events that move an application between statuses. Recorded verbatim in the audit trail.
/// </summary>
public enum WorkflowEvent
{
    ApplicationCreated,
    ValidationStarted,
    ValidationPassed,
    ValidationFailed,
    PhotoCheckPassed,
    PhotoCheckFailed,
    AutoApproved,
    ManualReviewRequired,
    ManuallyApproved,
    ManuallyRejected,
    ProcessingFailed
}
