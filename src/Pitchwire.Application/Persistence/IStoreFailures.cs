using Microsoft.EntityFrameworkCore;

namespace Pitchwire.Application.Persistence;

/// <summary>
/// Reads the meaning out of a store failure.
/// </summary>
/// <remarks>
/// Whether a failed write was a uniqueness violation is answered differently by every database, and
/// EF Core does not answer it for you. Asking through a port keeps the SQLSTATE where the provider
/// lives, and this is the one place the application layer would otherwise have to know it was talking
/// to PostgreSQL.
/// </remarks>
public interface IStoreFailures
{
    bool IsUniqueViolation(DbUpdateException failure);
}
