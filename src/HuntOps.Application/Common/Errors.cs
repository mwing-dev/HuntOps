namespace HuntOps.Application.Common;

/// <summary>The requested resource does not exist (or is not visible).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, object id)
        : base($"{resource} '{id}' was not found.")
    {
    }

    public NotFoundException()
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The request conflicts with current state (duplicate, stale version, archived parent, ...).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException()
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Input failed validation. <see cref="Errors"/> is keyed by camelCase field name.</summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public RequestValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public RequestValidationException()
        : this(new Dictionary<string, string[]>())
    {
    }

    public RequestValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
