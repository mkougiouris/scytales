namespace Scytales.Application.Abstractions;

/// <summary>Port to the national ID screening source (configuration today, a screening service in production).</summary>
public interface IWatchlist
{
    Task<bool> IsWatchlistedAsync(string? nationalId, CancellationToken cancellationToken);
}
