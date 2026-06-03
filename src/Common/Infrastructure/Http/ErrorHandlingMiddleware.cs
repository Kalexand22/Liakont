namespace Stratum.Common.Infrastructure.Http;

using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Middleware that catches unhandled exceptions and returns RFC 7807 ProblemDetails responses.
/// Domain exceptions (NotFoundException, ConflictException, DomainException) map to 4xx.
/// Unexpected exceptions map to 500 with structured logging and without leaking details.
/// </summary>
internal sealed partial class ErrorHandlingMiddleware
{
    // Static fields before instance fields (SA1204)
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    // Public methods before private (SA1202); instance before static within public (no static here)
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    // Private static methods before private instance methods (SA1204)
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Domain exception {ExceptionType}: {Message}")]
    private static partial void LogDomainException(ILogger logger, string exceptionType, string message);

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, exposeDetail) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found", true),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", true),
            DomainException => (StatusCodes.Status400BadRequest, "Bad Request", true),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", false),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", false),
        };

        if (statusCode >= 500)
        {
            LogUnhandledException(_logger, exception);
        }
        else
        {
            LogDomainException(_logger, exception.GetType().Name, exception.Message);
        }

        var problem = new ProblemDetailsPayload(
            Type: "about:blank",
            Title: title,
            Status: statusCode,
            Detail: exposeDetail ? exception.Message : null,
            Instance: context.Request.Path.Value);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = MediaTypeNames.Application.ProblemJson;

        await JsonSerializer.SerializeAsync(context.Response.Body, problem, SerializerOptions);
    }

    // Nested types last (SA1201)
    private sealed record ProblemDetailsPayload(
        string Type,
        string Title,
        int Status,
        string? Detail,
        string? Instance);
}
