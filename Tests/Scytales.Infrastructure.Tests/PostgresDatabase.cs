using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scytales.Infrastructure;
using Scytales.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Scytales.Infrastructure.Tests.PostgresDatabase))]

namespace Scytales.Infrastructure.Tests;

/// <summary>
/// PostgreSQL in a container, once per test run, with the real migrations applied, and the real
/// <see cref="DependencyInjection.AddInfrastructure"/> wiring on top. No broker and no hosts: these tests
/// check the persistence adapter itself.
/// </summary>
public sealed class PostgresDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public ServiceProvider Services { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Services = InfrastructureServices.Build(new() { ["ConnectionStrings:Postgres"] = _container.GetConnectionString() });
        await Services.MigrateDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null)
            await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>The services <see cref="DependencyInjection.AddInfrastructure"/> registers, built from the given settings.</summary>
internal static class InfrastructureServices
{
    public static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration) // as the hosts do: options bind from it (Watchlist)
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>For the services that never touch the database: a connection string is required, never used.</summary>
    public static ServiceProvider WithoutDatabase(Dictionary<string, string?>? settings = null) =>
        Build(new(settings ?? []) { ["ConnectionStrings:Postgres"] = "Host=unused" });
}
