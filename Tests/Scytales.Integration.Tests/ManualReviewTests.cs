using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Scytales.Integration.Tests;

/// <summary>FR-6: manual actions are only possible from <c>PendingManualReview</c>, and are recorded (FR-6.3).</summary>
public sealed class ManualReviewTests(ScytalesSystem system)
{
    private readonly HttpClient _api = system.Api;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Approve_from_review_returns_the_new_status_and_is_recorded()
    {
        var id = await _api.SubmitForReviewAsync();

        using var response = await _api.PostAsync($"/api/applications/{id}/approve", null, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<StatusResponse>(Cancellation))!.Status.ShouldBe("Approved");
        var last = (await _api.GetHistoryAsync(id))[^1];
        last.Event.ShouldBe("ManuallyApproved");
        last.FromStatus.ShouldBe("PendingManualReview");
        last.ToStatus.ShouldBe("Approved");
    }

    [Theory]
    [InlineData("Photo does not meet quality requirements")]
    [InlineData(null)] // the brief: reject "may accept a reason"
    public async Task Reject_from_review_records_the_reason_if_given(string? reason)
    {
        var id = await _api.SubmitForReviewAsync();

        using var response = reason is null
            ? await _api.PostAsync($"/api/applications/{id}/reject", null, Cancellation)
            : await _api.PostAsJsonAsync($"/api/applications/{id}/reject", new { reason }, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var last = (await _api.GetHistoryAsync(id))[^1];
        last.Event.ShouldBe("ManuallyRejected");
        last.FromStatus.ShouldBe("PendingManualReview");
        last.ToStatus.ShouldBe("Rejected");
        last.Details.ShouldBe(reason);
    }

    // Which statuses refuse is covered in the Domain tests; here, once per action, the refusal's HTTP shape.
    [Theory]
    [InlineData("approve.png", "Approved", "approve", "ManuallyApproved")]
    [InlineData("fail-low-quality.png", "Failed", "reject", "ManuallyRejected")]
    public async Task Manual_actions_outside_review_are_refused_with_409(string sample, string status, string action, string attempted)
    {
        var id = await _api.SubmitAndGetIdAsync(sample);
        (await _api.WaitForOutcomeAsync(id)).Status.ShouldBe(status);
        var historyBefore = await _api.GetHistoryAsync(id);

        using var response = await _api.PostAsync($"/api/applications/{id}/{action}", null, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        problem.RootElement.GetProperty("currentStatus").GetString().ShouldBe(status);
        problem.RootElement.GetProperty("attemptedAction").GetString().ShouldBe(attempted);
        (await _api.GetHistoryAsync(id)).ShouldBe(historyBefore);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Manual_actions_on_an_unknown_application_return_404(string action)
    {
        using var response = await _api.PostAsync($"/api/applications/{Guid.NewGuid()}/{action}", null, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
