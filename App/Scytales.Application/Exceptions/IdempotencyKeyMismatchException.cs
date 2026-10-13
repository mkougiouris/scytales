namespace Scytales.Application.Exceptions;

/// <summary>The <c>Idempotency-Key</c> was already used for a different request (BN-2).</summary>
public sealed class IdempotencyKeyMismatchException(string idempotencyKey)
    : Exception($"Idempotency key '{idempotencyKey}' was already used for a different request.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
