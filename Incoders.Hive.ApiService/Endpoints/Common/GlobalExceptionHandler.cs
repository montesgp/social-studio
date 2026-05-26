using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Common;

/// <summary>
/// Last-resort handler so NO unhandled exception ever reaches a user as a raw 500.
/// It logs the real error (so we can diagnose it) and returns an RFC 7807 problem carrying a
/// stable "code" the frontend maps to a friendly, non-technical message + support actions.
/// Specific, actionable errors (missing config / provider unreachable) are produced earlier by the
/// pre-flight gate and Result failures — this only catches the genuinely unexpected.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private const string UnexpectedErrorCode = "server.unexpected_error";

    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A client that aborted the request is not a server error.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        _logger.LogError(
            exception,
            "Unhandled exception handling {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var problem = new ProblemDetails
        {
            Title = "Unexpected error",
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://tools.ietf.org/html/rfc7807",
            Detail = _environment.IsDevelopment()
                ? $"{exception.GetType().Name}: {exception.Message}"
                : "An unexpected error occurred. Please try again, and contact support if it persists.",
        };
        problem.Extensions["code"] = UnexpectedErrorCode;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
