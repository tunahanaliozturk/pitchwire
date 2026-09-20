using System.Reflection;
using Pitchwire.Application.Ingestion;
using Pitchwire.Domain;
using Pitchwire.Infrastructure.Persistence;

namespace Pitchwire.UnitTests;

/// <summary>
/// The direction the dependencies are allowed to point.
/// </summary>
/// <remarks>
/// Layering that lives only in folder names is a convention, and conventions lose to deadlines. These
/// read what each assembly actually compiled against, so adding a using statement in the wrong
/// direction fails here rather than being noticed in review a month later.
/// </remarks>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Match).Assembly;
    private static readonly Assembly Application = typeof(EventIngestor).Assembly;
    private static readonly Assembly Infrastructure = typeof(PitchwireDbContext).Assembly;

    [Fact]
    public void The_domain_depends_on_nothing_of_ours()
    {
        // Not even the wire contract. The rules of football do not change because a provider renamed
        // a field, and this is what keeps that true.
        PitchwireReferencesOf(Domain).ShouldBeEmpty();
    }

    [Fact]
    public void The_domain_carries_no_framework_dependency()
    {
        var carried = Domain.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal))
            .ToList();

        carried.ShouldBeEmpty();
    }

    [Fact]
    public void The_application_layer_knows_nothing_of_infrastructure_or_the_host()
    {
        PitchwireReferencesOf(Application)
            .ShouldBe(["Pitchwire.Contracts", "Pitchwire.Domain"], ignoreOrder: true);
    }

    [Fact]
    public void The_application_layer_names_no_database_provider()
    {
        // It talks to a relational store through EF Core, and which store that is belongs one layer
        // further out. A reference to Npgsql here would make the port in front of it pointless.
        Application.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ShouldNotContain(name => name.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_depends_inward_and_not_on_the_host()
    {
        PitchwireReferencesOf(Infrastructure).ShouldNotContain("Pitchwire.Api");
    }

    private static List<string> PitchwireReferencesOf(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Pitchwire.", StringComparison.Ordinal))];
}
