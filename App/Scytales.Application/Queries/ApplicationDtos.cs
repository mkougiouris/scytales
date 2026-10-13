using System.Text.Json.Serialization;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.Queries;

/// <summary>FR-2 response shape.</summary>
public sealed record ApplicationStatusDto(
    Guid ApplicationId,
    ApplicationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ApplicationStatusDto From(LicenceApplication application) =>
        new(application.Id, application.Status, application.CreatedAt, application.UpdatedAt);
}

/// <summary>FR-7 response shape.</summary>
public sealed record ApplicationHistoryEntryDto(
    DateTimeOffset Timestamp,
    ApplicationStatus? FromStatus,
    ApplicationStatus ToStatus,
    WorkflowEvent Event,
    // Omitted when empty, as in the brief's example; FromStatus stays, since null there means "creation".
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Details);
