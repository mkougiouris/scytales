extern alias api;
extern alias worker;

using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Scytales.Application.Messages;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(Scytales.Integration.Tests.ScytalesSystem))]

namespace Scytales.Integration.Tests;

/// <summary>
/// The whole system, once per test run: PostgreSQL and RabbitMQ in containers, and the real API and Worker hosts
/// in-process. As in Docker Compose, the two hosts share nothing but the database and the broker.
/// </summary>
public sealed class ScytalesSystem : IAsyncLifetime
{
    private const string ErlangCookie = "scytales-integration-tests";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly RabbitMqContainer _queue = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        // Same workaround as Docker/docker-compose.yml: some Docker Desktop setups can't read .erlang.cookie.
        .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", $"-setcookie {ErlangCookie}")
        .WithEnvironment("RABBITMQ_CTL_ERL_ARGS", $"-setcookie {ErlangCookie}")
        .Build();

    private HostFactory<api::Program>? _api;
    private HostFactory<worker::Program>? _worker;

    /// <summary>Client for the API host.</summary>
    public HttpClient Api { get; private set; } = null!;

    /// <summary>Client for the Worker host (health endpoints only).</summary>
    public HttpClient Worker { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_database.StartAsync(), _queue.StartAsync());

        Dictionary<string, string?> settings = new()
        {
            ["ConnectionStrings:Postgres"] = _database.GetConnectionString(),
            ["ConnectionStrings:RabbitMq"] = _queue.GetConnectionString(),
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:MassTransit"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Migrations"] = "Warning"
        };

        // The API first: it applies the migrations on startup (D-13), before the worker touches the schema.
        // On real Kestrel rather than the in-memory TestServer, which does not enforce request size limits (413).
        _api = new HostFactory<api::Program>(settings);
        _api.UseKestrel(0);
        _api.StartServer();
        Api = _api.CreateClient();

        _worker = new HostFactory<worker::Program>(settings);
        Worker = _worker.CreateClient(); // starts the host, and with it the bus and its consumers
    }

    /// <summary>
    /// Publishes <see cref="ApplicationSubmitted"/> again for an existing application, as a broker redelivery or a
    /// duplicate publish would (RL-1), and completes once the worker has consumed it; throws if consuming it failed.
    /// </summary>
    public async Task PublishSubmittedAgainAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var bus = _worker!.Services.GetRequiredService<IBus>();
        var consumed = new ConsumedObserver(applicationId);
        using var observing = bus.ConnectConsumeMessageObserver(consumed);

        await bus.Publish(new ApplicationSubmitted(applicationId), cancellationToken);
        await consumed.Completion.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null) await _worker.DisposeAsync();
        if (_api is not null) await _api.DisposeAsync();
        await _queue.DisposeAsync();
        await _database.DisposeAsync();
    }

    private sealed class ConsumedObserver(Guid applicationId) : IConsumeMessageObserver<ApplicationSubmitted>
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public Task PreConsume(ConsumeContext<ApplicationSubmitted> context) => Task.CompletedTask;

        public Task PostConsume(ConsumeContext<ApplicationSubmitted> context)
        {
            if (context.Message.ApplicationId == applicationId)
                _completion.TrySetResult();
            return Task.CompletedTask;
        }

        public Task ConsumeFault(ConsumeContext<ApplicationSubmitted> context, Exception exception)
        {
            if (context.Message.ApplicationId == applicationId)
                _completion.TrySetException(exception);
            return Task.CompletedTask;
        }
    }

    private sealed class HostFactory<TProgram>(IReadOnlyDictionary<string, string?> settings)
        : WebApplicationFactory<TProgram> where TProgram : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("IntegrationTests");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        }
    }
}
