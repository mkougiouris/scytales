namespace Scytales.Application.Exceptions;

/// <summary>
/// The aggregate's <c>Version</c> no longer matched on save: another request or worker changed it first.
/// Raised by the persistence adapter so callers never depend on a specific ORM exception.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
