using Microsoft.Extensions.Logging;
using Scytales.Application.Abstractions;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.UseCases.Fail;

/// <summary>
/// RL-5: records a technical failure once processing has exhausted its retries (the message is in the
/// error queue). Idempotent and best-effort: an application that has already moved on is left alone.
/// </summary>
public sealed class FailApplicationHandler(
    IApplicationRepository applications,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<FailApplicationHandler> logger)
{
    public async Task HandleAsync(Guid applicationId, string reason, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken);
        if (application is null)
        {
            logger.LogWarning("Cannot record failure for unknown application {ApplicationId}: {Reason}", applicationId, reason);
            return;
        }

        if (!ApplicationWorkflow.TryGetTarget(application.Status, WorkflowEvent.ProcessingFailed, out _))
        {
            logger.LogInformation(
                "Application {ApplicationId} is already {Status}; processing failure not recorded: {Reason}",
                applicationId, application.Status, reason);
            return;
        }

        application.Fail(reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Application {ApplicationId} failed after retries were exhausted: {Reason}", applicationId, reason);
    }
}
