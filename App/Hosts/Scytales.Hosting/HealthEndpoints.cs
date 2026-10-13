using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scytales.Infrastructure;

namespace Scytales.Hosting;

public static class HealthEndpoints
{
    /// <summary>
    /// <c>/health/live</c>: the process is up (no dependencies checked, so a broker outage doesn't get it killed).
    /// <c>/health/ready</c>: every <c>ready</c> check passes. <c>informational</c> checks (the API's broker) are
    /// listed with their own status but do not affect the result.
    /// </summary>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check =>
                check.Tags.Contains(DependencyInjection.ReadyTag) || check.Tags.Contains(DependencyInjection.InformationalTag),
            ResponseWriter = WriteReadinessAsync
        });
    }

    private static Task WriteReadinessAsync(HttpContext context, HealthReport report)
    {
        // Decided here rather than by HealthCheckOptions.ResultStatusCodes, which would count informational checks.
        // Degraded (e.g. the worker's broker unreachable) means the instance can't do its work: not ready.
        var ready = report.Entries.Values
            .Where(entry => entry.Tags.Contains(DependencyInjection.ReadyTag))
            .All(entry => entry.Status == HealthStatus.Healthy);

        context.Response.StatusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = ready ? "Ready" : "NotReady",
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                affectsReadiness = e.Value.Tags.Contains(DependencyInjection.ReadyTag),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1)
            })
        });
    }
}
