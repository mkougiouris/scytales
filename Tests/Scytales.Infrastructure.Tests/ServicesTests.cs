using Microsoft.Extensions.DependencyInjection;
using Scytales.Application.Abstractions;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Infrastructure.Tests;

/// <summary>The simulated external services and the clock, as <c>AddInfrastructure</c> registers them.</summary>
public sealed class ServicesTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly ServiceProvider _services = InfrastructureServices.WithoutDatabase();

    public void Dispose() => _services.Dispose();

    private Task<int> ScoreAsync(byte[] content) =>
        _services.GetRequiredService<IPhotoQualityScorer>()
            .ScoreAsync(new ApplicationPhoto("face.jpg", "image/jpeg", content), Cancellation);

    [Fact]
    public async Task The_photo_score_is_the_same_for_the_same_bytes()
    {
        // D-5: a redelivered message must reach the same outcome (RL-1, RL-3).
        byte[] content = [1, 2, 3, 4, 5];

        (await ScoreAsync(content)).ShouldBe(await ScoreAsync([.. content]));
    }

    [Fact]
    public async Task Photo_scores_stay_between_0_and_100_and_spread_across_the_range()
    {
        var scores = await Task.WhenAll(Enumerable.Range(0, 500).Select(i => ScoreAsync(BitConverter.GetBytes(i))));

        scores.ShouldAllBe(score => score >= 0 && score <= 100);
        // All three outcomes need to happen naturally: below 30, 30-59 and 60 or above.
        scores.ShouldContain(score => score < 30);
        scores.ShouldContain(score => score >= 30 && score < 60);
        scores.ShouldContain(score => score >= 60);
    }

    [Theory]
    [InlineData("XX000000", true)]
    [InlineData(" xx000000 ", true)] // trimmed and case-insensitive
    [InlineData("AB123456", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task The_watchlist_matches_configured_national_ids(string? nationalId, bool expected)
    {
        using var services = InfrastructureServices.WithoutDatabase(new()
        {
            ["Watchlist:NationalIds:0"] = "XX000000",
            ["Watchlist:NationalIds:1"] = "  " // blank entries are ignored, never matched
        });

        (await services.GetRequiredService<IWatchlist>().IsWatchlistedAsync(nationalId, Cancellation)).ShouldBe(expected);
    }

    [Fact]
    public void The_clock_has_the_databases_microsecond_precision()
    {
        var now = _services.GetRequiredService<TimeProvider>().GetUtcNow();

        (now.Ticks % TimeSpan.TicksPerMicrosecond).ShouldBe(0);
        now.ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
