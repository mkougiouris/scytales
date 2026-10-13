using Scytales.Domain.Applications.Workflow;

namespace Scytales.Application.Abstractions;

/// <summary>Write-side access to the <see cref="LicenceApplication"/> aggregate.</summary>
public interface IApplicationRepository
{
    /// <summary>
    /// Loads the aggregate with its applicant details and photo. Past history entries are not loaded;
    /// new ones added through the aggregate are persisted on save.
    /// </summary>
    Task<LicenceApplication?> GetAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>The application submitted with this <c>Idempotency-Key</c>, if any, without loading the aggregate.</summary>
    Task<IdempotentSubmission?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    void Add(LicenceApplication application);
}

/// <summary>What a repeated submission needs to know about the application its key already created.</summary>
public sealed record IdempotentSubmission(Guid ApplicationId, ApplicationStatus Status, string RequestHash);
