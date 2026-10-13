using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scytales.Application.Exceptions;
using Scytales.Domain.Applications.Workflow;

namespace Scytales.Api.ErrorHandling;

/// <summary>
/// The single place exceptions become HTTP responses (D-7): RFC 9457 ProblemDetails for every error.
/// Expected outcomes (400/404/409/413/422) are not logged as errors; anything else is a 500 that reveals nothing.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ApplicationNotFoundException e => Problem(StatusCodes.Status404NotFound, "Application not found", e.Message),

            InvalidTransitionException e => Problem(StatusCodes.Status409Conflict, "Invalid state transition", e.Message,
                ("currentStatus", e.CurrentStatus.ToString()),
                ("attemptedAction", e.AttemptedEvent.ToString())),

            ConcurrencyConflictException e => Problem(StatusCodes.Status409Conflict, "Concurrent modification", e.Message),

            IdempotencyKeyMismatchException e => Problem(StatusCodes.Status422UnprocessableEntity, "Idempotency key reused", e.Message,
                ("idempotencyKey", e.IdempotencyKey)),

            BadHttpRequestException e => Problem(e.StatusCode,
                e.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Request too large" : "Bad request", e.Message),

            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred", detail: null);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    private static ProblemDetails Problem(int status, string title, string? detail, params (string Key, object? Value)[] extensions)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        foreach (var (key, value) in extensions)
            problem.Extensions[key] = value;
        return problem;
    }
}
