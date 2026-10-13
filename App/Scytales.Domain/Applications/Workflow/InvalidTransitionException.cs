namespace Scytales.Domain.Applications.Workflow;

/// <summary>The transition table (<see cref="ApplicationWorkflow"/>) does not allow this event in this status.</summary>
public sealed class InvalidTransitionException(Guid applicationId, ApplicationStatus currentStatus, WorkflowEvent attemptedEvent)
    : Exception($"Application {applicationId} cannot apply '{attemptedEvent}' while in status '{currentStatus}'.")
{
    public Guid ApplicationId { get; } = applicationId;
    public ApplicationStatus CurrentStatus { get; } = currentStatus;
    public WorkflowEvent AttemptedEvent { get; } = attemptedEvent;
}
