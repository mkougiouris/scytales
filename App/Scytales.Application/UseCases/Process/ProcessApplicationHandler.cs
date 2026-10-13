using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.UseCases.Process;

/// <summary>
/// Runs the automated workflow for one application, as triggered by <c>ApplicationSubmitted</c>.
/// <list type="bullet">
/// <item>One step at a time, saved after each, so a crash resumes from the last saved status (RL-3).</item>
/// <item>Driven by the current status, so a duplicate or redelivered message finds nothing to do (RL-1).</item>
/// <item>A concurrent worker makes the save fail with <see cref="ConcurrencyConflictException"/>; the retry
/// reloads and carries on from whatever that worker saved.</item>
/// </list>
/// </summary>
public sealed class ProcessApplicationHandler(
    IApplicationRepository applications,
    IUnitOfWork unitOfWork,
    IDataValidationPolicy dataValidationPolicy,
    IPhotoCheckPolicy photoCheckPolicy,
    IRiskAssessmentPolicy riskAssessmentPolicy,
    IPhotoQualityScorer photoQualityScorer,
    IWatchlist watchlist,
    TimeProvider clock,
    ILogger<ProcessApplicationHandler> logger)
{
    public async Task HandleAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken)
            ?? throw new ApplicationNotFoundException(applicationId);

        var stepsRun = 0;
        while (IsAutomated(application.Status))
        {
            // One span per step, its save included (D-18).
            using var step = WorkflowTelemetry.StartStep(application.Id, application.Status.ToString());

            await RunStepAsync(application, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            stepsRun++;

            var entry = application.History[^1];
            step.Completed(entry.Event.ToString(), entry.ToStatus.ToString(), entry.Details);
            logger.LogInformation(
                "Application {ApplicationId} moved {FromStatus} -> {ToStatus} on {WorkflowEvent} (v{Version}) {Details}",
                application.Id, entry.FromStatus, entry.ToStatus, entry.Event, entry.Version, entry.Details);
        }

        if (stepsRun == 0)
            logger.LogInformation("Application {ApplicationId} is already {Status}; nothing to process", application.Id, application.Status);
    }

    /// <summary>False when awaiting manual review or finished: no automated step applies.</summary>
    private static bool IsAutomated(ApplicationStatus status) => status is
        ApplicationStatus.Submitted or ApplicationStatus.ValidatingData or
        ApplicationStatus.CheckingPhoto or ApplicationStatus.RiskAssessment;

    private async Task RunStepAsync(LicenceApplication application, CancellationToken cancellationToken)
    {
        switch (application.Status)
        {
            case ApplicationStatus.Submitted:
                application.StartValidation(clock.GetUtcNow());
                break;

            case ApplicationStatus.ValidatingData:
                application.ValidateData(dataValidationPolicy, clock.GetUtcNow());
                break;

            case ApplicationStatus.CheckingPhoto:
                int? score = application.Photo is { } photo
                    ? await photoQualityScorer.ScoreAsync(photo, cancellationToken)
                    : null;
                application.CheckPhoto(score, photoCheckPolicy, clock.GetUtcNow());
                break;

            case ApplicationStatus.RiskAssessment:
                var isWatchlisted = await watchlist.IsWatchlistedAsync(application.Applicant.NationalId, cancellationToken);
                application.AssessRisk(isWatchlisted, riskAssessmentPolicy, clock.GetUtcNow());
                break;

            default:
                throw new UnreachableException($"No automated step for status {application.Status}.");
        }
    }
}
