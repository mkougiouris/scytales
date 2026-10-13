namespace Scytales.Domain.Applications.ValueObjects;

/// <summary>
/// The client's <c>Idempotency-Key</c> for the submission that created the application (BN-2), with a hash of
/// that request, so a retry with the same key can be told apart from a different request reusing it.
/// </summary>
public sealed record IdempotencyKey
{
    public const int MaxLength = 255;

    /// <summary>Length of a hex-encoded SHA-256 hash.</summary>
    public const int RequestHashLength = 64;

    public string Key { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;

    private IdempotencyKey() { }

    public IdempotencyKey(string key, string requestHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, MaxLength, nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentOutOfRangeException.ThrowIfNotEqual(requestHash.Length, RequestHashLength, nameof(requestHash));

        Key = key;
        RequestHash = requestHash;
    }
}
