namespace Scytales.Application.Abstractions;

/// <summary>
/// Publishes integration messages through the transactional outbox: a message is only sent if the
/// <see cref="IUnitOfWork"/> it was published in commits.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken) where TMessage : class;
}
