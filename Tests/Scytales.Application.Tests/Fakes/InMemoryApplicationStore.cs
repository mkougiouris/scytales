using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;

namespace Scytales.Application.Tests.Fakes;

/// <summary>
/// Repository, unit of work and outbox publisher in one, so tests can assert what was added,
/// published and saved, and in which order.
/// </summary>
internal sealed class InMemoryApplicationStore : IApplicationRepository, IUnitOfWork, IMessagePublisher
{
    private readonly Dictionary<Guid, LicenceApplication> _applications = [];
    private readonly List<LicenceApplication> _added = [];
    private readonly List<object> _publishedSinceSave = [];

    public List<string> Calls { get; } = [];
    public List<object> Published { get; } = [];
    public int Saves { get; private set; }

    /// <summary>When set, every save throws as if another writer changed the aggregate first.</summary>
    public bool ConflictOnSave { get; set; }

    /// <summary>Runs at the start of the next save only: lets a test commit a concurrent writer first.</summary>
    public Action? BeforeNextSave { get; set; }

    public IReadOnlyCollection<LicenceApplication> Applications => _applications.Values;

    /// <summary>Puts an aggregate in storage without recording a call or a save.</summary>
    public LicenceApplication Seed(LicenceApplication application)
    {
        _applications[application.Id] = application;
        return application;
    }

    public Task<LicenceApplication?> GetAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        Calls.Add("Get");
        return Task.FromResult(_applications.GetValueOrDefault(applicationId));
    }

    public Task<IdempotentSubmission?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        Calls.Add("FindByIdempotencyKey");
        var found = _applications.Values.SingleOrDefault(a => !_added.Contains(a) && a.IdempotencyKey?.Key == idempotencyKey);
        return Task.FromResult(found is null ? null : new IdempotentSubmission(found.Id, found.Status, found.IdempotencyKey!.RequestHash));
    }

    public void Add(LicenceApplication application)
    {
        Calls.Add("Add");
        _applications[application.Id] = application;
        _added.Add(application);
    }

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken) where TMessage : class
    {
        Calls.Add($"Publish:{typeof(TMessage).Name}");
        Published.Add(message);
        _publishedSinceSave.Add(message);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Calls.Add("Save");
        BeforeNextSave?.Invoke();
        BeforeNextSave = null;

        if (ConflictOnSave)
            throw new ConcurrencyConflictException("Simulated version mismatch.");

        // The unique index on the idempotency key: the transaction rolls back, outbox messages included.
        if (_added.Any(TakesAStoredKey))
        {
            Rollback();
            throw new DuplicateIdempotencyKeyException();
        }

        _added.Clear();
        _publishedSinceSave.Clear();
        Saves++;
        return Task.CompletedTask;
    }

    private bool TakesAStoredKey(LicenceApplication added) =>
        added.IdempotencyKey is { } key
        && _applications.Values.Any(a => a.Id != added.Id && !_added.Contains(a) && a.IdempotencyKey?.Key == key.Key);

    private void Rollback()
    {
        foreach (var application in _added)
            _applications.Remove(application.Id);
        foreach (var message in _publishedSinceSave)
            Published.Remove(message);
        _added.Clear();
        _publishedSinceSave.Clear();
    }
}
