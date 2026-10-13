using System.Net;
using System.Net.Http.Json;

namespace Scytales.Integration.Tests;

/// <summary>The reliability answers of D-8, exercised against the real database and broker.</summary>
public sealed class ReliabilityTests(ScytalesSystem system)
{
    private readonly HttpClient _api = system.Api;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("approve.png", "Approved")]
    [InlineData("manual-review.png", "PendingManualReview")]
    [InlineData("fail-low-quality.png", "Failed")]
    public async Task The_same_message_processed_twice_changes_nothing(string sample, string outcome)
    {
        // RL-1: the consumer is idempotent by state, so a redelivery after the outcome is a no-op.
        var id = await _api.SubmitAndGetIdAsync(sample);
        var before = await _api.WaitForOutcomeAsync(id);
        before.Status.ShouldBe(outcome);
        var historyBefore = await _api.GetHistoryAsync(id);

        await system.PublishSubmittedAgainAsync(id, Cancellation);

        (await _api.GetFromJsonAsync<StatusResponse>($"/api/applications/{id}", Cancellation)).ShouldBe(before);
        (await _api.GetHistoryAsync(id)).ShouldBe(historyBefore);
    }

    [Fact]
    public async Task Concurrent_approve_and_reject_let_exactly_one_win()
    {
        // RL-4: the transition table and the Version token mean only one of two racing reviewers succeeds.
        var id = await _api.SubmitForReviewAsync();

        var responses = await Task.WhenAll(
            _api.PostAsync($"/api/applications/{id}/approve", null, Cancellation),
            _api.PostAsJsonAsync($"/api/applications/{id}/reject", new { reason = "Racing the approval" }, Cancellation));

        try
        {
            responses.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);

            var status = (await _api.GetFromJsonAsync<StatusResponse>($"/api/applications/{id}", Cancellation))!.Status;
            var manual = (await _api.GetHistoryAsync(id))
                .Where(e => e.Event is "ManuallyApproved" or "ManuallyRejected")
                .ShouldHaveSingleItem();
            manual.ToStatus.ShouldBe(status);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }
}
