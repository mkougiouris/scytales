using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Scytales.Integration.Tests;

/// <summary>BN-2: the <c>Idempotency-Key</c> header on submit, against the real unique index.</summary>
public sealed class IdempotencyTests(ScytalesSystem system)
{
    private readonly HttpClient _api = system.Api;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string NewKey() => $"test-{Guid.NewGuid()}";

    [Fact]
    public async Task Repeating_a_submission_with_its_key_returns_the_same_application_once()
    {
        var key = NewKey();

        using var first = await _api.SubmitAsync(idempotencyKey: key);
        var id = (await first.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation))!.ApplicationId;
        await _api.WaitForOutcomeAsync(id);
        using var repeat = await _api.SubmitAsync(idempotencyKey: key);

        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        repeat.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        repeat.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        repeat.Headers.Location!.ToString().ShouldBe($"/api/applications/{id}");
        var replayed = await repeat.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation);
        replayed!.ApplicationId.ShouldBe(id);
        replayed.Status.ShouldBe("Approved"); // the current status, not the original response
        (await _api.GetHistoryAsync(id)).Count(e => e.Event == "ApplicationCreated").ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_submissions_with_the_same_key_create_one_application()
    {
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _api.SubmitAsync(idempotencyKey: key)));

        try
        {
            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);
            responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
            var ids = await Task.WhenAll(responses.Select(async r =>
                (await r.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation))!.ApplicationId));
            var id = ids.Distinct().ShouldHaveSingleItem();

            // One application, processed once: only the winner's message reached the worker.
            (await _api.WaitForOutcomeAsync(id)).Status.ShouldBe("Approved");
            (await _api.GetHistoryAsync(id)).Select(e => e.Event).ShouldBe(
                ["ApplicationCreated", "ValidationStarted", "ValidationPassed", "PhotoCheckPassed", "AutoApproved"]);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_refused_with_422()
    {
        var key = NewKey();
        using var first = await _api.SubmitAsync(idempotencyKey: key);
        first.EnsureSuccessStatusCode();

        using var different = await _api.SubmitAsync(
            idempotencyKey: key, fields: new Dictionary<string, string?> { ["email"] = "someone.else@example.com" });

        different.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        different.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var problem = JsonDocument.Parse(await different.Content.ReadAsStringAsync(Cancellation));
        problem.RootElement.GetProperty("idempotencyKey").GetString().ShouldBe(key);
    }

    [Fact]
    public async Task Different_keys_create_different_applications()
    {
        using var first = await _api.SubmitAsync(idempotencyKey: NewKey());
        using var second = await _api.SubmitAsync(idempotencyKey: NewKey());

        var firstId = (await first.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation))!.ApplicationId;
        var secondId = (await second.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation))!.ApplicationId;
        firstId.ShouldNotBe(secondId);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("256 characters")]
    public async Task An_invalid_key_is_refused_with_400(string key)
    {
        if (key == "256 characters")
            key = new string('k', 256);

        using var response = await _api.SubmitAsync(idempotencyKey: key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
