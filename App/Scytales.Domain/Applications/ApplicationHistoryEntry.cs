using Scytales.Domain.Applications.Workflow;

namespace Scytales.Domain.Applications;

/// <summary>
/// One audit-trail record. <see cref="Version"/> is the application's version after the transition,
/// so (ApplicationId, Version) is unique and gives a stable ordering.
/// </summary>
public sealed class ApplicationHistoryEntry
{
    public Guid ApplicationId { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public ApplicationStatus? FromStatus { get; private set; }
    public ApplicationStatus ToStatus { get; private set; }
    public WorkflowEvent Event { get; private set; }
    public string? Details { get; private set; }

    private ApplicationHistoryEntry() { }

    internal ApplicationHistoryEntry(
        Guid applicationId,
        long version,
        DateTimeOffset timestamp,
        ApplicationStatus? fromStatus,
        ApplicationStatus toStatus,
        WorkflowEvent @event,
        string? details)
    {
        ApplicationId = applicationId;
        Version = version;
        Timestamp = timestamp;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Event = @event;
        Details = details;
    }
}
