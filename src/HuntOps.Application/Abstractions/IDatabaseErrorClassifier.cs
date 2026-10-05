namespace HuntOps.Application.Abstractions;

public enum DatabaseErrorKind
{
    UniqueViolation,
    CheckViolation,
    ForeignKeyViolation,
    RestrictViolation,
    ConcurrencyConflict,
}

public sealed record DatabaseError(DatabaseErrorKind Kind, string? Constraint);

/// <summary>Turns provider-specific database exceptions into provider-neutral constraint information.</summary>
public interface IDatabaseErrorClassifier
{
    DatabaseError? Classify(Exception exception);
}
