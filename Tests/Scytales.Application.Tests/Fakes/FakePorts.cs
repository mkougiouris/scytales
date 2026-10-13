using Scytales.Application.Abstractions;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Application.Tests.Fakes;

internal sealed class FakePhotoQualityScorer(int score = TestData.GoodScore) : IPhotoQualityScorer
{
    public int Score { get; set; } = score;
    public List<ApplicationPhoto> Scored { get; } = [];

    public Task<int> ScoreAsync(ApplicationPhoto photo, CancellationToken cancellationToken)
    {
        Scored.Add(photo);
        return Task.FromResult(Score);
    }
}

internal sealed class FakeWatchlist(params string[] nationalIds) : IWatchlist
{
    public List<string?> Queried { get; } = [];

    public Task<bool> IsWatchlistedAsync(string? nationalId, CancellationToken cancellationToken)
    {
        Queried.Add(nationalId);
        return Task.FromResult(nationalId is not null && nationalIds.Contains(nationalId));
    }
}
