using System.Diagnostics;

namespace Scytales.Application;

/// <summary>
/// Spans for the workflow steps (D-18), from the BCL's <see cref="ActivitySource"/>: the layer takes no
/// OpenTelemetry dependency. They are only recorded when a listener (the hosts' OpenTelemetry setup) subscribes.
/// </summary>
public static class WorkflowTelemetry
{
    public const string SourceName = "Scytales.Workflow";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>One automated step, from the status it starts in until its transition is saved.</summary>
    internal static Activity? StartStep(Guid applicationId, string fromStatus)
    {
        var activity = Source.StartActivity($"workflow {fromStatus}");
        activity?.SetTag("application.id", applicationId);
        activity?.SetTag("workflow.from_status", fromStatus);
        return activity;
    }

    /// <summary>Records where the step ended; called once its transition is saved.</summary>
    internal static void Completed(this Activity? activity, string workflowEvent, string toStatus, string? details)
    {
        activity?.SetTag("workflow.event", workflowEvent);
        activity?.SetTag("workflow.to_status", toStatus);
        if (details is not null)
            activity?.SetTag("workflow.details", details);
    }
}
