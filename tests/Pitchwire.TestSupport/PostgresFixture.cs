using Microsoft.EntityFrameworkCore;
using Pitchwire.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pitchwire.TestSupport;

/// <summary>
/// A real PostgreSQL instance for the tests that need one.
/// </summary>
/// <remarks>
/// The in-memory provider is not an option here. The guarantee under test is a unique index refusing
/// a second insert, and a provider that does not enforce indexes would only prove that the provider
/// was configured.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("pitchwire")
        .WithUsername("pitchwire")
        .WithPassword("pitchwire")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public PitchwireDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PitchwireDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PitchwireDbContext(options);
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
