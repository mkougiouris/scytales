using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Scytales.Api.Endpoints;
using Scytales.Api.ErrorHandling;
using Scytales.Api.OpenApi;
using Scytales.Api.Serialization;
using Scytales.Application;
using Scytales.Hosting;
using Scytales.Infrastructure;
using Scytales.Infrastructure.Messaging;
using Scytales.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("scytales-api");

// D-1: the API's only protective limit. Everything else about the upload is judged by the workflow.
var maxRequestBodyBytes = builder.Configuration.GetValue("Api:MaxRequestBodyBytes", 5L * 1024 * 1024);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMessaging(builder.Configuration, MessagingMode.Publisher);

builder.Services.AddHealthChecks().AddInfrastructureHealthChecks();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
});
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxRequestBodyBytes);

builder.Services.AddOpenApi(OpenApiConfiguration.Configure);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(); // empty error responses (e.g. binding failures) also get a ProblemDetails body

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Driving Licence Onboarding API v1");
    options.RoutePrefix = "swagger";
});
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapHealthEndpoints();
app.MapApplicationEndpoints(maxRequestBodyBytes);

// The API owns the schema (D-13): the worker never migrates, so the two can't race.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await app.Services.MigrateDatabaseAsync();

app.Run();
