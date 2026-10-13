using MassTransit;
using Scytales.Application.Abstractions;

namespace Scytales.Infrastructure.Messaging;

/// <summary>
/// With the bus outbox enabled, the scoped <see cref="IPublishEndpoint"/> writes to the outbox table through
/// the same DbContext, so the message commits (or not) with the unit of work (RL-2).
/// </summary>
internal sealed class MassTransitMessagePublisher(IPublishEndpoint publishEndpoint) : IMessagePublisher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken) where TMessage : class =>
        publishEndpoint.Publish(message, cancellationToken);
}
