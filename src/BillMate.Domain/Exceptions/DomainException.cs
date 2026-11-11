namespace BillMate.Domain.Exceptions;

/// <summary>
/// Represents a domain rule violation with a standardized BillMate error code.
/// </summary>
public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}
