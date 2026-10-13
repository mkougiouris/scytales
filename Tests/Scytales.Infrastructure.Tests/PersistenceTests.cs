using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;
using Scytales.Application.Queries;
using Scytales.Domain.Applications.Policies;
using Scytales.Domain.Applications.ValueObjects;
using Scytales.Domain.Applications.Workflow;
using Scytales.Infrastructure.Persistence;

namespace Scytales.Infrastructure.Tests;

/// <summary>
/// The persistence adapter against real PostgreSQL: the EF mapping, and the database errors it must translate
/// into the Application layer's exceptions (D-4, D-17).
/// </summary>
public sealed class PersistenceTests(PostgresDatabase database)
{
    private static readonly WorkflowRules Rules = new();
    private static readonly string Hash = new('a', IdempotencyKey.RequestHashLength);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saving_a_stale_version_throws_ConcurrencyConflictException()
    {
        // Two reviewers load the same version and both pass the transition check in memory: only the
        // database's Version check can stop the second. This is the overlap an HTTP race rarely produces.
        var id = await StoreAsync(PendingManualReview());
        await using var first = Scope();
        await using var second = Scope();
        var approving = await first.LoadAsync(id);
        var rejecting = await second.LoadAsync(id);

        approving.Approve(DateTimeOffset.UtcNow);
        await first.SaveAsync();
        rejecting.Reject("Too late", DateTimeOffset.UtcNow);

        await Should.ThrowAsync<ConcurrencyConflictException>(second.SaveAsync);

        var history = await QueryAsync(q => q.GetHistoryAsync(id, Cancellation));
        history!.Where(e => e.Event is WorkflowEvent.ManuallyApproved or WorkflowEvent.ManuallyRejected)
            .ShouldHaveSingleItem().Event.ShouldBe(WorkflowEvent.ManuallyApproved);
        (await QueryAsync(q => q.GetStatusAsync(id, Cancellation)))!.Status.ShouldBe(ApplicationStatus.Approved);
    }

    [Fact]
    public async Task A_row_changed_behind_the_aggregates_back_is_caught_by_the_Version_token()
    {
        // The test above is stopped by the history key (one entry per version), which EF inserts before it
        // updates the application. A change that bumps the version without writing history (a manual fix, a
        // future code path) reaches the UPDATE ... WHERE version = @original, and must be refused there too.
        var id = await StoreAsync(PendingManualReview());
        await using var scope = Scope();
        var stale = await scope.LoadAsync(id);
        await scope.ExecuteSqlAsync($"UPDATE applications SET version = version + 1 WHERE id = {id}");

        stale.Approve(DateTimeOffset.UtcNow);

        await Should.ThrowAsync<ConcurrencyConflictException>(scope.SaveAsync);
    }

    [Fact]
    public async Task Saving_a_second_application_with_the_same_idempotency_key_throws_DuplicateIdempotencyKeyException()
    {
        var key = $"key-{Guid.NewGuid()}";
        var firstId = await StoreAsync(Submitted(new IdempotencyKey(key, Hash)));
        await using var scope = Scope();
        var duplicate = Submitted(new IdempotencyKey(key, Hash));
        scope.Repository.Add(duplicate);

        await Should.ThrowAsync<DuplicateIdempotencyKeyException>(scope.SaveAsync);

        (await QueryAsync(q => q.GetStatusAsync(duplicate.Id, Cancellation))).ShouldBeNull();
        (await QueryAsync(q => q.GetStatusAsync(firstId, Cancellation))).ShouldNotBeNull();
    }

    [Fact]
    public async Task Applications_without_a_key_never_collide()
    {
        await StoreAsync(Submitted());

        await Should.NotThrowAsync(() => StoreAsync(Submitted()));
    }

    [Fact]
    public async Task An_idempotency_key_finds_its_application_its_current_status_and_request_hash()
    {
        var key = $"key-{Guid.NewGuid()}";
        var application = Submitted(new IdempotencyKey(key, Hash));
        application.StartValidation(DateTimeOffset.UtcNow);
        var id = await StoreAsync(application);
        await using var scope = Scope();

        var found = await scope.Repository.FindByIdempotencyKeyAsync(key, Cancellation);

        found.ShouldBe(new IdempotentSubmission(id, ApplicationStatus.ValidatingData, Hash));
        (await scope.Repository.FindByIdempotencyKeyAsync("no-such-key", Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task The_aggregate_round_trips_with_its_owned_values()
    {
        var key = new IdempotencyKey($"key-{Guid.NewGuid()}", Hash);
        var original = Submitted(key);
        var id = await StoreAsync(original);
        await using var scope = Scope();

        var loaded = await scope.LoadAsync(id);

        loaded.Applicant.ShouldBe(original.Applicant);
        loaded.Photo.ShouldBe(original.Photo); // bytes included
        loaded.IdempotencyKey.ShouldBe(key);
        loaded.Status.ShouldBe(ApplicationStatus.Submitted);
        loaded.Version.ShouldBe(1);
        loaded.CreatedAt.ShouldBe(original.CreatedAt);
    }

    private static LicenceApplication Submitted(IdempotencyKey? key = null) =>
        LicenceApplication.Submit(
            new ApplicantDetails("John", "Doe", new DateOnly(1990, 5, 20), "AB123456", "john.doe@example.com",
                "+306900000000", "Example Street 10", "B"),
            new ApplicationPhoto("face.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, .. new byte[20_000]]),
            MicrosecondNow(),
            key);

    // PostgreSQL stores microseconds: a finer timestamp would not compare equal after a round trip.
    private static DateTimeOffset MicrosecondNow()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static LicenceApplication PendingManualReview()
    {
        var application = Submitted();
        var now = application.CreatedAt;
        application.StartValidation(now);
        application.ValidateData(new DataValidationPolicy(Rules), now);
        application.CheckPhoto(90, new PhotoCheckPolicy(Rules), now);
        application.AssessRisk(isWatchlisted: true, new RiskAssessmentPolicy(Rules), now);
        application.Status.ShouldBe(ApplicationStatus.PendingManualReview);
        return application;
    }

    private async Task<Guid> StoreAsync(LicenceApplication application)
    {
        await using var scope = Scope();
        scope.Repository.Add(application);
        await scope.SaveAsync();
        return application.Id;
    }

    private UnitOfWorkScope Scope() => new(database.Services.CreateAsyncScope());

    private async Task<T> QueryAsync<T>(Func<IApplicationQueries, Task<T>> query)
    {
        await using var scope = database.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IApplicationQueries>());
    }

    /// <summary>One request's worth of persistence: its own DbContext, as in the hosts.</summary>
    private sealed class UnitOfWorkScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public IApplicationRepository Repository { get; } = scope.ServiceProvider.GetRequiredService<IApplicationRepository>();

        public async Task<LicenceApplication> LoadAsync(Guid id) =>
            await Repository.GetAsync(id, Cancellation) ?? throw new InvalidOperationException($"Application {id} not found.");

        public Task SaveAsync() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Cancellation);

        /// <summary>Another writer, bypassing the aggregate: on its own connection, so it commits at once.</summary>
        public async Task ExecuteSqlAsync(FormattableString sql)
        {
            await using var other = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            await other.ServiceProvider.GetRequiredService<ScytalesDbContext>().Database.ExecuteSqlInterpolatedAsync(sql, Cancellation);
        }

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
