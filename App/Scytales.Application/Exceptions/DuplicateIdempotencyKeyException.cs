namespace Scytales.Application.Exceptions;

/// <summary>
/// The save lost a race: another request with the same <c>Idempotency-Key</c> stored its application first.
/// Raised by the persistence adapter so callers never depend on a specific ORM exception.
/// </summary>
public sealed class DuplicateIdempotencyKeyException(Exception? innerException = null)
    : Exception("An application was already submitted with the same idempotency key.", innerException);
