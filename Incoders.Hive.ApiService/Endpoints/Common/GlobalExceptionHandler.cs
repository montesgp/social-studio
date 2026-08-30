using System.Data.Common;
using System.Net.Sockets;
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
    private const string DataStoreUnavailableCode = "server.data_store_unavailable";

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

        var problem = IsDataStoreUnavailable(exception)
            ? BuildDataStoreUnavailableProblem()
            : BuildUnexpectedErrorProblem(exception);

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    /// <summary>
    /// The data store being unreachable is an availability problem, not a bug in the request:
    /// it maps to 503 so clients (and probes) can tell "retry later" apart from "this is broken".
    /// Detection stays provider-agnostic — <see cref="DbException.IsTransient"/> plus raw transport
    /// failures — so no database-specific type leaks into the API layer.
    /// </summary>
    private static bool IsDataStoreUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true } or SocketException)
            {
                return true;
            }
        }

        return false;
    }

    private static ProblemDetails BuildDataStoreUnavailableProblem()
    {
        // Never environment-dependent: connection failures carry host, port and user in their
        // message, so the sanitized text is the only text we ever return — dev included.
        var problem = new ProblemDetails
        {
            Title = "Service temporarily unavailable",
            Status = StatusCodes.Status503ServiceUnavailable,
            Type = "https://tools.ietf.org/html/rfc7807",
            Detail = "The service is temporarily unavailable. Please try again in a few moments.",
        };
        problem.Extensions["code"] = DataStoreUnavailableCode;
        return problem;
    }

    private ProblemDetails BuildUnexpectedErrorProblem(Exception exception)
    {
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
        return problem;
    }
}
