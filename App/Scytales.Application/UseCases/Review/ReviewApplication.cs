using Microsoft.Extensions.Logging;
using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;
using Scytales.Application.Queries;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.UseCases.Review;

/// <summary>
/// FR-6 manual decisions. Runs synchronously in the HTTP request, so the caller gets an immediate answer:
/// <see cref="ApplicationNotFoundException"/> (404), <see cref="InvalidTransitionException"/> or
/// <see cref="ConcurrencyConflictException"/> (409).
/// </summary>
public sealed class ReviewApplicationHandler(
    IApplicationRepository applications,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReviewApplicationHandler> logger)
{
    public Task<ApplicationStatusDto> ApproveAsync(Guid applicationId, CancellationToken cancellationToken) =>
        DecideAsync(applicationId, application => application.Approve(clock.GetUtcNow()), cancellationToken);

    public Task<ApplicationStatusDto> RejectAsync(Guid applicationId, string? reason, CancellationToken cancellationToken) =>
        DecideAsync(applicationId, application => application.Reject(reason, clock.GetUtcNow()), cancellationToken);

    private async Task<ApplicationStatusDto> DecideAsync(
        Guid applicationId,
        Action<LicenceApplication> decide,
        CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken)
            ?? throw new ApplicationNotFoundException(applicationId);

        decide(application);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var entry = application.History[^1];
        logger.LogInformation(
            "Application {ApplicationId} reviewed: {WorkflowEvent} (v{Version}) {Details}",
            application.Id, entry.Event, entry.Version, entry.Details);

        return ApplicationStatusDto.From(application);
    }
}
