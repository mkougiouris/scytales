using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scytales.Application.Abstractions;
using Scytales.Application.Exceptions;
using Scytales.Application.Queries;
using Scytales.Infrastructure.Messaging;
using Scytales.Infrastructure.Messaging.Consumers;
using Scytales.Infrastructure.Persistence;
using Scytales.Infrastructure.Services;

namespace Scytales.Infrastructure;

public static class DependencyInjection
{
    private const string PostgresConnectionName = "Postgres";
    private const string RabbitMqConnectionName = "RabbitMq";

    /// <summary>Persistence (EF Core on PostgreSQL), the read side and the simulated external services.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = RequiredConnectionString(configuration, PostgresConnectionName);

        services.AddDbContext<ScytalesDbContext>(options => ScytalesDbContext.Configure(options, connectionString));
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IApplicationQueries, ApplicationQueries>();
        // Replace, not TryAdd: wins over AddApplication's default clock whichever is registered first.
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new StoragePrecisionTimeProvider(TimeProvider.System)));

        services.AddSingleton<IPhotoQualityScorer, HashPhotoQualityScorer>();
        services.AddOptions<WatchlistOptions>().BindConfiguration(WatchlistOptions.SectionName);
        services.AddSingleton<IWatchlist, ConfigurationWatchlist>();

        return services;
    }

    /// <summary>
    /// MassTransit on RabbitMQ (D-3). The API publishes through the EF Core transactional outbox; the worker
    /// consumes with exponential retry, after which the message goes to the <c>_error</c> queue and a
    /// <see cref="Fault{T}"/> marks the application Failed.
    /// </summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration, MessagingMode mode)
    {
        var rabbitMq = new Uri(RequiredConnectionString(configuration, RabbitMqConnectionName));
        var retry = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            if (mode == MessagingMode.Publisher)
            {
                bus.AddEntityFrameworkOutbox<ScytalesDbContext>(outbox =>
                {
                    outbox.UsePostgres();
                    outbox.UseBusOutbox();
                });

                // Thanks to the outbox, the API keeps accepting submissions while the broker is down; they are
                // delivered once it is back. So the broker is reported, but does not decide the API's readiness.
                bus.ConfigureHealthCheckOptions(options =>
                {
                    options.Tags.Remove(ReadyTag);
                    options.Tags.Add(InformationalTag);
                });
            }

            if (mode == MessagingMode.Consumer)
            {
                // No consumer inbox: each workflow step commits on its own, and duplicates are made harmless by
                // status-driven idempotency plus the Version token (see implementation-steps D-3).
                bus.AddConsumer<ApplicationSubmittedConsumer>();
                bus.AddConsumer<ApplicationSubmittedFaultConsumer>();

                bus.AddConfigureEndpointsCallback((_, _, endpoint) =>
                    endpoint.UseMessageRetry(r =>
                    {
                        r.Exponential(retry.RetryLimit, retry.RetryMinInterval, retry.RetryMaxInterval, retry.RetryIntervalDelta);
                        // Retrying cannot make a missing application appear: dead-letter it straight away.
                        r.Ignore<ApplicationNotFoundException>();
                    }));
            }

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(rabbitMq);
                rabbit.ConfigureEndpoints(context);
            });
        });

        // Registered in both modes: AddApplication registers every use case, so the container must be able to build
        // them all (Development validates this on startup). Only the publisher mode goes through the outbox; the
        // worker never submits, and a publish from it would go straight to the broker.
        services.AddScoped<IMessagePublisher, MassTransitMessagePublisher>();

        return services;
    }

    /// <summary>Tag for checks that must pass before a host should receive work.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Tag for checks that are reported alongside readiness but do not decide it.</summary>
    public const string InformationalTag = "informational";

    /// <summary>
    /// Readiness of the database. MassTransit registers its own bus check when <see cref="AddMessaging"/> is
    /// used: tagged <c>ready</c> in the worker, <c>informational</c> in the API.
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<ScytalesDbContext>("postgres", tags: [ReadyTag]);

    private static string RequiredConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Connection string '{name}' is not configured.");
}
