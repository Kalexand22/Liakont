namespace Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist.
/// Maps to HTTP 404 Not Found.
/// </summary>
public class NotFoundException : DomainException
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string entityName, object id)
        : base($"{entityName} '{id}' was not found.")
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
