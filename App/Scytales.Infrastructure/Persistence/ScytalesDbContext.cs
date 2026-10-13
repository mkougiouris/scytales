using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Scytales.Domain.Applications;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Infrastructure.Persistence;

public sealed class ScytalesDbContext(DbContextOptions<ScytalesDbContext> options) : DbContext(options)
{
    public DbSet<LicenceApplication> Applications => Set<LicenceApplication>();
    public DbSet<ApplicationHistoryEntry> ApplicationHistory => Set<ApplicationHistoryEntry>();

    /// <summary>Shared by the runtime registration and the design-time factory so migrations match the app.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.ConfigureDataSource(dataSource => dataSource.ConfigureTracing(tracing => tracing
                // Only trace commands that are part of something (a request, a message, a workflow step, D-18):
                // the outbox poller and the health checks would otherwise flood the dashboard with lone queries.
                // Opening pooled connections is noise too.
                .ConfigureCommandFilter(_ => Activity.Current is not null)
                .ConfigureBatchFilter(_ => Activity.Current is not null)
                .EnablePhysicalOpenTracing(false))))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScytalesDbContext).Assembly);

        // MassTransit transactional outbox tables (D-3): messages published through IPublishEndpoint are
        // written here in the same SaveChanges as the aggregate, then delivered to RabbitMQ.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Readable in the database and stable if enum members are ever reordered.
        configurationBuilder.Properties<ApplicationStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<WorkflowEvent>().HaveConversion<string>().HaveMaxLength(32);
    }
}
