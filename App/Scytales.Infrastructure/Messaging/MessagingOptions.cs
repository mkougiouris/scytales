namespace Scytales.Infrastructure.Messaging;

/// <summary>Retry policy for consumers (RL-6). Section <c>Messaging</c>.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public int RetryLimit { get; set; } = 5;
    public TimeSpan RetryMinInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetryMaxInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryIntervalDelta { get; set; } = TimeSpan.FromSeconds(2);
}

public enum MessagingMode
{
    /// <summary>The API: publishes through the transactional outbox, consumes nothing.</summary>
    Publisher,

    /// <summary>The worker: consumes and processes applications.</summary>
    Consumer
}
