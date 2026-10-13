using System.Net.Http.Json;

namespace Scytales.Integration.Tests;

/// <summary>The API as a client sees it: HTTP and JSON only, no references to the application's own types.</summary>
internal static class ApiClient
{
    private static readonly string[] AutomatedStatuses = ["Submitted", "ValidatingData", "CheckingPhoto", "RiskAssessment"];

    /// <summary>On the watchlist in appsettings (D-6).</summary>
    public const string WatchlistedNationalId = "XX000000";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, string> DefaultFields = new()
    {
        ["firstName"] = "John",
        ["lastName"] = "Doe",
        ["dateOfBirth"] = "1990-05-20",
        ["nationalId"] = "AB123456",
        ["email"] = "john.doe@example.com",
        ["phoneNumber"] = "+306900000000",
        ["address"] = "Example Street 10",
        ["licenceCategory"] = "B"
    };

    /// <param name="fields">Replace default form fields; a null value leaves the field out.</param>
    public static async Task<HttpResponseMessage> SubmitAsync(
        this HttpClient api,
        string? sample = "approve.png",
        IReadOnlyDictionary<string, string?>? fields = null,
        string? idempotencyKey = null,
        HttpContent? photo = null,
        bool expectContinue = false)
    {
        using var form = new MultipartFormDataContent();
        foreach (var name in DefaultFields.Keys.Union(fields?.Keys ?? []))
            if (Field(name, fields) is { } value)
                form.Add(new StringContent(value), name);

        photo ??= sample is null ? null : new ByteArrayContent(await File.ReadAllBytesAsync(SamplePath(sample), Cancellation));
        if (photo is not null)
            form.Add(photo, "photo", sample ?? "photo.png");

        // Expect: 100-continue lets the server refuse a request (e.g. 413) before the client sends the body.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/applications") { Content = form };
        request.Headers.ExpectContinue = expectContinue;
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await api.SendAsync(request, Cancellation);
    }

    public static async Task<Guid> SubmitAndGetIdAsync(
        this HttpClient api, string? sample = "approve.png", IReadOnlyDictionary<string, string?>? fields = null)
    {
        using var response = await api.SubmitAsync(sample, fields);
        response.EnsureSuccessStatusCode();
        var submitted = await response.Content.ReadFromJsonAsync<SubmittedResponse>(Cancellation);
        return submitted!.ApplicationId;
    }

    /// <summary>Submits a watchlisted applicant and waits until the application is pending manual review.</summary>
    public static async Task<Guid> SubmitForReviewAsync(this HttpClient api)
    {
        var id = await api.SubmitAndGetIdAsync(fields: new Dictionary<string, string?> { ["nationalId"] = WatchlistedNationalId });
        (await api.WaitForOutcomeAsync(id)).Status.ShouldBe("PendingManualReview");
        return id;
    }

    /// <summary>Polls the status until the worker has finished its automated steps.</summary>
    public static async Task<StatusResponse> WaitForOutcomeAsync(this HttpClient api, Guid applicationId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (true)
        {
            var status = await api.GetFromJsonAsync<StatusResponse>($"/api/applications/{applicationId}", timeout.Token);
            if (!AutomatedStatuses.Contains(status!.Status))
                return status;
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    public static async Task<HistoryEntry[]> GetHistoryAsync(this HttpClient api, Guid applicationId) =>
        (await api.GetFromJsonAsync<HistoryEntry[]>($"/api/applications/{applicationId}/history", Cancellation))!;

    public static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);

    private static string? Field(string name, IReadOnlyDictionary<string, string?>? fields) =>
        fields is not null && fields.TryGetValue(name, out var value) ? value : DefaultFields[name];
}

internal sealed record SubmittedResponse(Guid ApplicationId, string Status);

internal sealed record StatusResponse(Guid ApplicationId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

internal sealed record HistoryEntry(DateTimeOffset Timestamp, string? FromStatus, string ToStatus, string Event, string? Details);
