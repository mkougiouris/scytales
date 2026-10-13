using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Scytales.Integration.Tests;

/// <summary>
/// End to end through the real hosts: HTTP → API → outbox → RabbitMQ → worker → PostgreSQL → HTTP.
/// Each test submits its own applications, so the tests are independent of each other and of their order.
/// </summary>
public sealed class ApplicationWorkflowTests(ScytalesSystem system)
{
    private readonly HttpClient _api = system.Api;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_returns_202_with_the_initial_status_and_the_status_location()
    {
        using var response = await _api.SubmitAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var submitted = await response.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation);
        submitted!.Status.ShouldBe("Submitted");
        response.Headers.Location!.ToString().ShouldBe($"/api/applications/{submitted.ApplicationId}");
    }

    [Fact]
    public async Task Clean_application_is_approved_automatically_with_a_complete_audit_trail()
    {
        var id = await _api.SubmitAndGetIdAsync();

        var outcome = await _api.WaitForOutcomeAsync(id);

        outcome.Status.ShouldBe("Approved");
        var history = await _api.GetHistoryAsync(id);
        history.Select(e => e.Event).ShouldBe(
            ["ApplicationCreated", "ValidationStarted", "ValidationPassed", "PhotoCheckPassed", "AutoApproved"]);
        history[0].FromStatus.ShouldBeNull();
        history.Skip(1).Select(e => e.FromStatus).ShouldBe(history.SkipLast(1).Select(e => e.ToStatus));
        history[^1].Timestamp.ShouldBe(outcome.UpdatedAt);
    }

    [Fact]
    public async Task Responses_follow_the_briefs_json_shape()
    {
        var id = await _api.SubmitAndGetIdAsync();
        await _api.WaitForOutcomeAsync(id);

        using var status = JsonDocument.Parse(await _api.GetStringAsync($"/api/applications/{id}", Cancellation));
        using var history = JsonDocument.Parse(await _api.GetStringAsync($"/api/applications/{id}/history", Cancellation));

        status.RootElement.GetProperty("createdAt").GetString().ShouldEndWith("Z");
        var created = history.RootElement[0];
        created.GetProperty("fromStatus").ValueKind.ShouldBe(JsonValueKind.Null); // kept: null means "creation"
        created.TryGetProperty("details", out _).ShouldBeFalse();                 // omitted when empty
    }

    [Theory]
    [InlineData("manual-review.png", "PendingManualReview", "Photo quality score 40 is below the review threshold of 60")]
    [InlineData("fail-low-quality.png", "Failed", "Photo quality score 15 is below the minimum of 30")]
    [InlineData("fail-too-small.png", "Failed", "File size 331 bytes is below the minimum of 10240 bytes")]
    [InlineData("fail-unsupported-type.bmp", "Failed", "File type is unsupported")]
    [InlineData(null, "Failed", "No photo was provided")]
    public async Task Sample_photos_lead_to_their_documented_outcome(string? sample, string expectedStatus, string expectedDetails)
    {
        var id = await _api.SubmitAndGetIdAsync(sample: sample);

        (await _api.WaitForOutcomeAsync(id)).Status.ShouldBe(expectedStatus);
        (await _api.GetHistoryAsync(id))[^1].Details.ShouldStartWith(expectedDetails);
    }

    [Fact]
    public async Task Photo_above_the_request_limit_is_refused_with_413()
    {
        // Without 100-continue the server answers 413 and closes the connection while the client is still
        // uploading, which HttpClient reports as a socket error rather than the response.
        using var response = await _api.SubmitAsync(
            photo: new ByteArrayContent(new byte[6 * 1024 * 1024]), expectContinue: true);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Unknown_application_returns_404_problem_details()
    {
        using var response = await _api.GetAsync($"/api/applications/{Guid.NewGuid()}", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
