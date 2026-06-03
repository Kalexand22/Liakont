namespace Stratum.Common.Infrastructure.Http;

using Microsoft.AspNetCore.Builder;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds RFC 7807-compliant error handling middleware.
    /// Maps DomainException to 400, NotFoundException to 404, ConflictException to 409,
    /// UnauthorizedAccessException to 401, and unhandled exceptions to 500.
    /// Must be registered early in the pipeline (before UseAuthentication).
    /// </summary>
    public static IApplicationBuilder UseStratumErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
