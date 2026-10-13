using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Scytales.Application.Tests.Fakes;
using Scytales.Application.UseCases.Fail;
using Scytales.Application.UseCases.Process;
using Scytales.Application.UseCases.Review;
using Scytales.Application.UseCases.Submit;

namespace Scytales.Application.Tests;

/// <summary>The handlers under test, wired to in-memory fakes, the default policies and a fake clock.</summary>
internal sealed class Harness
{
    public InMemoryApplicationStore Store { get; } = new();
    public FakePhotoQualityScorer Scorer { get; } = new();
    public FakeWatchlist Watchlist { get; }
    public FakeTimeProvider Clock { get; } = new(TestData.Now.AddHours(1));

    public Harness(params string[] watchlistedIds) => Watchlist = new FakeWatchlist(watchlistedIds);

    public SubmitApplicationHandler Submit() =>
        new(Store, Store, Store, Clock, NullLogger<SubmitApplicationHandler>.Instance);

    public ProcessApplicationHandler Process() =>
        new(Store, Store, TestData.DataPolicy, TestData.PhotoPolicy, TestData.RiskPolicy, Scorer, Watchlist, Clock,
            NullLogger<ProcessApplicationHandler>.Instance);

    public ReviewApplicationHandler Review() =>
        new(Store, Store, Clock, NullLogger<ReviewApplicationHandler>.Instance);

    public FailApplicationHandler Fail() =>
        new(Store, Store, Clock, NullLogger<FailApplicationHandler>.Instance);
}
