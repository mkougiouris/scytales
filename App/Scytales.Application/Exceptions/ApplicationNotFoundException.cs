namespace Scytales.Application.Exceptions;

public sealed class ApplicationNotFoundException(Guid applicationId)
    : Exception($"Application {applicationId} was not found.")
{
    public Guid ApplicationId { get; } = applicationId;
}
