using ActionLedger.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// AD-20 — <c>DbUpdateConcurrencyException</c> and unique-constraint violations become
/// <see cref="ConcurrencyConflictException"/> here, which the Api maps to 409 <c>conflict</c>.
/// </summary>
/// <remarks>
/// This is the boundary AD-1 depends on: an EF Core or Npgsql exception must never reach
/// Application or Api, or the two inner rings would need to reference the provider to catch it.
/// </remarks>
internal static class ConcurrencyTranslation
{
    /// <summary>PostgreSQL <c>unique_violation</c>. Any other SQLSTATE is not a conflict.</summary>
    public const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    /// <summary>
    /// The <see cref="ConcurrencyConflictException"/> this exception should surface as, or
    /// <c>null</c> when it is not a conflict and must be left to propagate unchanged.
    /// </summary>
    public static ConcurrencyConflictException? Translate(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException concurrency => new ConcurrencyConflictException(
            "The record changed after it was read. Reload it and try again.",
            concurrency),

        DbUpdateException update when FindUniqueViolation(update) is { } violation => new ConcurrencyConflictException(
            $"That value is already taken ({violation.ConstraintName ?? "unique constraint"}).",
            update),

        _ => null,
    };

    private static PostgresException? FindUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: UniqueViolation } violation)
            {
                return violation;
            }
        }

        return null;
    }
}
