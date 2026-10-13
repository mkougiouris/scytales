using Microsoft.Extensions.Options;
using Scytales.Application.Abstractions;

namespace Scytales.Infrastructure.Services;

public sealed class WatchlistOptions
{
    public const string SectionName = "Watchlist";

    public List<string> NationalIds { get; set; } = [];
}

/// <summary>Config-backed watchlist (D-11). In production this would query a screening service.</summary>
internal sealed class ConfigurationWatchlist(IOptions<WatchlistOptions> options) : IWatchlist
{
    private readonly HashSet<string> _nationalIds = options.Value.NationalIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public Task<bool> IsWatchlistedAsync(string? nationalId, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(nationalId) && _nationalIds.Contains(nationalId.Trim()));
}
