using MassTransit;
using Scytales.Application.Messages;
using Scytales.Application.UseCases.Fail;

namespace Scytales.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit publishes <see cref="Fault{T}"/> once processing has exhausted its retries and the original
/// message has been moved to the error queue. Records the failure on the application (RL-5).
/// </summary>
internal sealed class ApplicationSubmittedFaultConsumer(FailApplicationHandler handler) : IConsumer<Fault<ApplicationSubmitted>>
{
    public Task Consume(ConsumeContext<Fault<ApplicationSubmitted>> context)
    {
        var error = context.Message.Exceptions.FirstOrDefault();
        var reason = error is null
            ? "Processing failed after retries were exhausted"
            : $"Processing failed after retries were exhausted: {error.ExceptionType}: {error.Message}";

        return handler.HandleAsync(context.Message.Message.ApplicationId, reason, context.CancellationToken);
    }
}
