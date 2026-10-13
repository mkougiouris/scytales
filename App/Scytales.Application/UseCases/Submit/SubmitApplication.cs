using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;
using Scytales.Application.Messages;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.UseCases.Submit;

/// <summary>
/// FR-1. Fields are taken as submitted, incomplete or not: judging them is the workflow's job (D-1).
/// </summary>
/// <param name="IdempotencyKey">Optional; a repeat with the same key returns the first application (BN-2).</param>
public sealed record SubmitApplicationCommand(
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? NationalId,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? LicenceCategory,
    PhotoUpload? Photo,
    string? IdempotencyKey = null);

public sealed record PhotoUpload(string FileName, string ContentType, byte[] Content);

/// <param name="Replayed">True when the idempotency key had already created this application; not serialised.</param>
public sealed record SubmitApplicationResult(
    Guid ApplicationId,
    ApplicationStatus Status,
    [property: JsonIgnore] bool Replayed = false);

public sealed class SubmitApplicationHandler(
    IApplicationRepository applications,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SubmitApplicationHandler> logger)
{
    public async Task<SubmitApplicationResult> HandleAsync(SubmitApplicationCommand command, CancellationToken cancellationToken)
    {
        var idempotencyKey = command.IdempotencyKey is { } key
            ? new IdempotencyKey(key, SubmissionRequestHash.Compute(command))
            : null;

        // A retry: answer with the application the first request created, without storing or publishing anything.
        if (idempotencyKey is not null
            && await applications.FindByIdempotencyKeyAsync(idempotencyKey.Key, cancellationToken) is { } existing)
            return Replay(existing, idempotencyKey);

        var applicant = new ApplicantDetails(
            command.FirstName,
            command.LastName,
            command.DateOfBirth,
            command.NationalId,
            command.Email,
            command.PhoneNumber,
            command.Address,
            command.LicenceCategory);

        var photo = command.Photo is { } upload
            ? new ApplicationPhoto(upload.FileName, upload.ContentType, upload.Content)
            : null;

        var application = LicenceApplication.Submit(applicant, photo, clock.GetUtcNow(), idempotencyKey);

        // Application, photo, first history entry and the outbox message commit together (RL-2).
        applications.Add(application);
        await publisher.PublishAsync(new ApplicationSubmitted(application.Id), cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // A concurrent request with the same key committed first; our application and its message rolled back.
            var winner = await applications.FindByIdempotencyKeyAsync(idempotencyKey!.Key, cancellationToken)
                ?? throw new InvalidOperationException($"Idempotency key '{idempotencyKey.Key}' was taken but its application was not found.");
            return Replay(winner, idempotencyKey);
        }

        logger.LogInformation("Application {ApplicationId} submitted (photo: {HasPhoto})", application.Id, photo is not null);

        return new SubmitApplicationResult(application.Id, application.Status);
    }

    private SubmitApplicationResult Replay(IdempotentSubmission existing, IdempotencyKey idempotencyKey)
    {
        if (existing.RequestHash != idempotencyKey.RequestHash)
            throw new IdempotencyKeyMismatchException(idempotencyKey.Key);

        logger.LogInformation("Repeated submission for application {ApplicationId} answered from its idempotency key", existing.ApplicationId);
        return new SubmitApplicationResult(existing.ApplicationId, existing.Status, Replayed: true);
    }
}
