namespace Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Base class for all domain-level exceptions in Stratum.
/// Maps to HTTP 400 Bad Request by default.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
