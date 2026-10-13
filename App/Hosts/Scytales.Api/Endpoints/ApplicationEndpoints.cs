using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Scytales.Api.Contracts;
using Scytales.Application.Exceptions;
using Scytales.Application.Queries;
using Scytales.Application.UseCases.Review;
using Scytales.Application.UseCases.Submit;
using Scytales.Domain.Applications.ValueObjects;

namespace Scytales.Api.Endpoints;

/// <summary>
/// FR-1, FR-2, FR-6, FR-7. Endpoints only translate HTTP to use cases; errors are thrown and turned into
/// ProblemDetails by <see cref="ErrorHandling.ApiExceptionHandler"/>.
/// </summary>
public static class ApplicationEndpoints
{
    /// <summary>Optional on submit (BN-2): a repeat with the same key returns the first application.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Set on a submit answered from an earlier request with the same idempotency key.</summary>
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder endpoints, long maxRequestBodyBytes)
    {
        var applications = endpoints.MapGroup("/api/applications").WithTags("Applications");

        applications.MapPost("/", SubmitAsync)
            .WithName("SubmitApplication")
            .WithSummary("Submit a driving licence application")
            .WithDescription(
                "Stores the application and its photo, then processes it asynchronously. " +
                "Poll the URL in the Location header for progress. Send an Idempotency-Key header to make retries safe: " +
                "a repeat with the same key returns the first application (with Idempotent-Replayed: true), " +
                "and reusing a key for a different request is refused with 422.")
            // Token-less JSON/multipart API without cookie auth: antiforgery does not apply.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(maxRequestBodyBytes))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        applications.MapGet("/{applicationId:guid}", GetStatusAsync)
            .WithName("GetApplicationStatus")
            .WithSummary("Get the current status of an application")
            .ProducesProblem(StatusCodes.Status404NotFound);

        applications.MapGet("/{applicationId:guid}/history", GetHistoryAsync)
            .WithName("GetApplicationHistory")
            .WithSummary("Get the audit trail of an application, oldest first")
            .ProducesProblem(StatusCodes.Status404NotFound);

        applications.MapPost("/{applicationId:guid}/approve", ApproveAsync)
            .WithName("ApproveApplication")
            .WithSummary("Approve an application that is pending manual review")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        applications.MapPost("/{applicationId:guid}/reject", RejectAsync)
            .WithName("RejectApplication")
            .WithSummary("Reject an application that is pending manual review, optionally with a reason")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<Accepted<SubmitApplicationResult>> SubmitAsync(
        [FromForm] SubmitApplicationForm form,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        SubmitApplicationHandler handler,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        // Validated here, at the edge, so a bad header is a 400 rather than a domain argument error.
        if (idempotencyKey is not null && (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKey.MaxLength))
            throw new BadHttpRequestException(
                $"The {IdempotencyKeyHeader} header must be 1 to {IdempotencyKey.MaxLength} characters and not blank.");

        var photo = form.Photo is { } file
            ? new PhotoUpload(file.FileName, file.ContentType, await ReadAllBytesAsync(file, cancellationToken))
            : null;

        var result = await handler.HandleAsync(
            new SubmitApplicationCommand(
                form.FirstName,
                form.LastName,
                form.DateOfBirth,
                form.NationalId,
                form.Email,
                form.PhoneNumber,
                form.Address,
                form.LicenceCategory,
                photo,
                idempotencyKey),
            cancellationToken);

        if (result.Replayed)
            response.Headers[IdempotentReplayedHeader] = "true";

        return TypedResults.Accepted($"/api/applications/{result.ApplicationId}", result);
    }

    private static async Task<Ok<ApplicationStatusDto>> GetStatusAsync(
        Guid applicationId,
        IApplicationQueries queries,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.GetStatusAsync(applicationId, cancellationToken)
            ?? throw new ApplicationNotFoundException(applicationId));

    private static async Task<Ok<IReadOnlyList<ApplicationHistoryEntryDto>>> GetHistoryAsync(
        Guid applicationId,
        IApplicationQueries queries,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.GetHistoryAsync(applicationId, cancellationToken)
            ?? throw new ApplicationNotFoundException(applicationId));

    private static async Task<Ok<ApplicationStatusDto>> ApproveAsync(
        Guid applicationId,
        ReviewApplicationHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.ApproveAsync(applicationId, cancellationToken));

    private static async Task<Ok<ApplicationStatusDto>> RejectAsync(
        Guid applicationId,
        [FromBody] RejectApplicationRequest? request,
        ReviewApplicationHandler handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.RejectAsync(applicationId, request?.Reason, cancellationToken));

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
