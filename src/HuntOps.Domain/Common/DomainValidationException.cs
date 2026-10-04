namespace HuntOps.Domain.Common;

/// <summary>A domain rule was violated. <see cref="Errors"/> is keyed by field name (camelCase, matching the API).</summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more domain rules were violated.")
    {
        Errors = errors;
    }

    public DomainValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public DomainValidationException()
        : this(new Dictionary<string, string[]>())
    {
    }

    public DomainValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
