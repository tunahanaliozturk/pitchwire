using Testcontainers.Redis;
using Xunit;

namespace Pitchwire.TestSupport;

/// <summary>
/// A real Redis, for the tests that are about the second level of the cache.
/// </summary>
/// <remarks>
/// Tag invalidation is the reason this exists. Whether dropping a tag reaches entries held outside
/// the process is a property of the cache implementation and the version in use, and an in-memory
/// substitute would prove only that the substitute behaves.
/// </remarks>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:8-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
