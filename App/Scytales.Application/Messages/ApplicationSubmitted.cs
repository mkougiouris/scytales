namespace Scytales.Application.Messages;

/// <summary>
/// Published when an application has been stored. Carries only the id: the worker loads
/// everything else, so the message never holds personal data or photo bytes.
/// </summary>
public sealed record ApplicationSubmitted(Guid ApplicationId);
