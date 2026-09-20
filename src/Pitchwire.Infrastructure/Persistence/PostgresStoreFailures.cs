using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pitchwire.Application.Persistence;

namespace Pitchwire.Infrastructure.Persistence;

internal sealed class PostgresStoreFailures : IStoreFailures
{
    /// <summary>SQLSTATE 23505, unique_violation.</summary>
    private const string UniqueViolation = "23505";

    public bool IsUniqueViolation(DbUpdateException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.InnerException is PostgresException { SqlState: UniqueViolation };
    }
}
