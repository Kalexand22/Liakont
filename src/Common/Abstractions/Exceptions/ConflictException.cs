namespace Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Thrown when an operation cannot be completed due to a conflicting state
/// (e.g., duplicate record, optimistic concurrency violation).
/// Maps to HTTP 409 Conflict.
/// </summary>
public class ConflictException : DomainException
{
    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
