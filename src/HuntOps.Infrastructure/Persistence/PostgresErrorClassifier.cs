using HuntOps.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HuntOps.Infrastructure.Persistence;

internal sealed class PostgresErrorClassifier : IDatabaseErrorClassifier
{
    public DatabaseError? Classify(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return new DatabaseError(DatabaseErrorKind.ConcurrencyConflict, null);
        }

        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
        return postgres?.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new DatabaseError(DatabaseErrorKind.UniqueViolation, postgres.ConstraintName),
            PostgresErrorCodes.CheckViolation => new DatabaseError(DatabaseErrorKind.CheckViolation, postgres.ConstraintName),
            PostgresErrorCodes.ForeignKeyViolation => new DatabaseError(DatabaseErrorKind.ForeignKeyViolation, postgres.ConstraintName),
            PostgresErrorCodes.RestrictViolation => new DatabaseError(DatabaseErrorKind.RestrictViolation, postgres.ConstraintName),
            _ => null,
        };
    }
}
