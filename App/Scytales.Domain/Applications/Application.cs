using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;
using static Scytales.Domain.Applications.Workflow.ApplicationStatus;
using static Scytales.Domain.Applications.Workflow.WorkflowEvent;

namespace Scytales.Domain.Applications;

/// <summary>
/// The aggregate root. Status changes only through <see cref="Apply"/>, which enforces
/// <see cref="ApplicationWorkflow"/>, bumps <see cref="Version"/> and records the history entry.
/// </summary>
public sealed class Application
{
    private readonly List<ApplicationHistoryEntry> _history = [];

    public Guid Id { get; private set; }
    public ApplicantDetails Applicant { get; private set; } = null!;

    /// <summary>Null when no photo was uploaded; judged by the photo check, not at submission.</summary>
    public ApplicationPhoto? Photo { get; private set; }

    /// <summary>Set when the client sent an <c>Idempotency-Key</c> with the submission (BN-2).</summary>
    public IdempotencyKey? IdempotencyKey { get; private set; }

    public ApplicationStatus Status { get; private set; }

    /// <summary>Set by the photo check, read by the risk assessment.</summary>
    public int? PhotoQualityScore { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every transition.</summary>
    public long Version { get; private set; }

    /// <summary>
    /// Entries recorded through this instance. When loaded from storage this need not contain
    /// the full trail; read the audit trail through a query instead.
    /// </summary>
    public IReadOnlyList<ApplicationHistoryEntry> History => _history;

    private Application() { }

    public static Application Submit(
        ApplicantDetails applicant, ApplicationPhoto? photo, DateTimeOffset now, IdempotencyKey? idempotencyKey = null)
    {
        ArgumentNullException.ThrowIfNull(applicant);
        now = now.ToUniversalTime();

        var application = new Application
        {
            Id = Guid.CreateVersion7(now),
            Applicant = applicant,
            Photo = photo,
            IdempotencyKey = idempotencyKey,
            Status = Submitted,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };
        application._history.Add(new ApplicationHistoryEntry(application.Id, application.Version, now, null, Submitted, ApplicationCreated, null));
        return application;
    }

    public void StartValidation(DateTimeOffset now) => Apply(ValidationStarted, null, now);

    // Age rules are judged on the submission date (CreatedAt), so a re-run always reaches the same answer.
    public void ValidateData(IDataValidationPolicy policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var result = policy.Evaluate(Applicant, CreatedAt);
        Apply(result.Passed ? ValidationPassed : ValidationFailed, result.Summary, now);
    }

    /// <param name="qualityScore">
    /// 0–100, from the (simulated) quality service; required when there is a <see cref="Photo"/>, ignored otherwise.
    /// </param>
    public void CheckPhoto(int? qualityScore, IPhotoCheckPolicy policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        EnsureCanApply(PhotoCheckPassed); // a wrong state is reported before a bad argument
        if (Photo is not null && qualityScore is not (>= 0 and <= 100))
            throw new ArgumentOutOfRangeException(nameof(qualityScore), qualityScore, "A photo needs a quality score between 0 and 100.");

        var score = Photo is null ? null : qualityScore;
        var result = policy.Evaluate(Photo, score);

        Apply(result.Passed ? PhotoCheckPassed : PhotoCheckFailed, result.Summary, now);
        PhotoQualityScore = score;
    }

    /// <param name="isWatchlisted">Whether the applicant's national ID is on the watchlist, looked up by the caller.</param>
    public void AssessRisk(bool isWatchlisted, IRiskAssessmentPolicy policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var result = policy.Evaluate(new RiskAssessmentInput(Applicant, CreatedAt, PhotoQualityScore, isWatchlisted));
        Apply(result.Passed ? AutoApproved : ManualReviewRequired, result.Summary, now);
    }

    public void Approve(DateTimeOffset now) => Apply(ManuallyApproved, null, now);

    public void Reject(string? reason, DateTimeOffset now) =>
        Apply(ManuallyRejected, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), now);

    /// <summary>Technical failure after retries are exhausted (RL-5).</summary>
    public void Fail(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Apply(ProcessingFailed, reason.Trim(), now);
    }

    private void EnsureCanApply(WorkflowEvent @event)
    {
        if (!ApplicationWorkflow.TryGetTarget(Status, @event, out _))
            throw new InvalidTransitionException(Id, Status, @event);
    }

    private void Apply(WorkflowEvent @event, string? details, DateTimeOffset now)
    {
        if (!ApplicationWorkflow.TryGetTarget(Status, @event, out var target))
            throw new InvalidTransitionException(Id, Status, @event);

        now = now.ToUniversalTime();
        var from = Status;
        Status = target;
        UpdatedAt = now;
        Version++;
        _history.Add(new ApplicationHistoryEntry(Id, Version, now, from, target, @event, details));
    }
}
