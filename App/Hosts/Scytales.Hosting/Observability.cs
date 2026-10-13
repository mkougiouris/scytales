using MassTransit.Logging;
using MassTransit.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scytales.Application;

namespace Scytales.Hosting;

/// <summary>
/// OpenTelemetry for both hosts (D-18): traces, metrics and logs, exported over OTLP only when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (Docker Compose points it at the dashboard). Without it, nothing
/// leaves the process, so tests and a plain <c>dotnet run</c> need no collector.
/// </summary>
public static class Observability
{
    // Npgsql (and so EF Core's queries) and MassTransit publish their own ActivitySource and Meter.
    private const string NpgsqlSource = "Npgsql";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes every few seconds would bury the traces worth looking at.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddSource(DiagnosticHeaders.DefaultListenerName)
                .AddSource(NpgsqlSource)
                .AddSource(WorkflowTelemetry.SourceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(InstrumentationOptions.MeterName)
                .AddMeter(NpgsqlSource));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            telemetry.UseOtlpExporter();

        return builder;
    }
}
