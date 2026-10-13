using System.Net;
using System.Text.Json;

namespace Scytales.Integration.Tests;

/// <summary>BN-6: both hosts report live and ready once their dependencies are up.</summary>
public sealed class HealthTests(ScytalesSystem system)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private HttpClient Host(string host) => host == "api" ? system.Api : system.Worker;

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    public async Task Hosts_report_live(string host)
    {
        using var response = await Host(host).GetAsync("/health/live", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("Healthy");
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    public async Task Hosts_report_ready_with_their_checks(string host)
    {
        using var response = await Host(host).GetAsync("/health/ready", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        report.RootElement.GetProperty("status").GetString().ShouldBe("Ready");
        report.RootElement.GetProperty("checks").EnumerateArray()
            .Where(c => c.GetProperty("affectsReadiness").GetBoolean())
            .ShouldAllBe(c => c.GetProperty("status").GetString() == "Healthy");
    }
}
