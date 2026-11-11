namespace BillMate.Application.Common.Exceptions;

public class AppException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatus { get; }

    public AppException(string errorCode, string message, int httpStatus = 400) : base(message)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
    }
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string errorCode, string message) : base(errorCode, message, 404) { }
}

public sealed class UnauthorizedAppException : AppException
{
    public UnauthorizedAppException(string errorCode, string message) : base(errorCode, message, 401) { }
}

public sealed class ForbiddenAppException : AppException
{
    public ForbiddenAppException(string errorCode, string message) : base(errorCode, message, 403) { }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string errorCode, string message) : base(errorCode, message, 409) { }
}

public sealed class ValidationAppException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base("VAL_000", "One or more validation errors occurred.", 422)
    {
        Errors = errors;
    }
}
