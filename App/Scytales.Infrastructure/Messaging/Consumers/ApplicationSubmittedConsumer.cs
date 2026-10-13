using MassTransit;
using Scytales.Application.Messages;
using Scytales.Application.UseCases.Process;

namespace Scytales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Thin adapter: idempotency and crash-resume live in <see cref="ProcessApplicationHandler"/>; retries and
/// dead-lettering are configured on the endpoint.
/// </summary>
internal sealed class ApplicationSubmittedConsumer(ProcessApplicationHandler handler) : IConsumer<ApplicationSubmitted>
{
    public Task Consume(ConsumeContext<ApplicationSubmitted> context) =>
        handler.HandleAsync(context.Message.ApplicationId, context.CancellationToken);
}
