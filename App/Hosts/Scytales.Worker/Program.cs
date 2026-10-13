using Scytales.Application;
using Scytales.Hosting;
using Scytales.Infrastructure;
using Scytales.Infrastructure.Messaging;

// The worker: consumes ApplicationSubmitted and runs the workflow (D-3). It's a web host only so it can expose
// health endpoints for Docker/orchestrators; it serves no other HTTP traffic. Migrations are the API's job.
var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("scytales-worker");

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMessaging(builder.Configuration, MessagingMode.Consumer);

builder.Services.AddHealthChecks().AddInfrastructureHealthChecks();

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();
