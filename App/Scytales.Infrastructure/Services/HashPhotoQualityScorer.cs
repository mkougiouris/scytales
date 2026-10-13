using System.Buffers.Binary;
using System.Security.Cryptography;
using Scytales.Application.Abstractions;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Infrastructure.Services;

/// <summary>
/// Simulated quality service (D-5): a score from 0 to 100 derived from the SHA-256 of the photo bytes.
/// Deterministic, so a redelivered message reaches the same outcome, yet spread evenly across photos.
/// </summary>
internal sealed class HashPhotoQualityScorer : IPhotoQualityScorer
{
    public Task<int> ScoreAsync(ApplicationPhoto photo, CancellationToken cancellationToken)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(photo.Content, hash);

        var score = (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % 101);
        return Task.FromResult(score);
    }
}
