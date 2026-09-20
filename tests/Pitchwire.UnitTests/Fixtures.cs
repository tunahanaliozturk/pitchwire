using Pitchwire.Domain;

namespace Pitchwire.UnitTests;

/// <summary>
/// A match with two known teams, and events built against it. Every helper returns a fresh instance,
/// so a test cannot be affected by what an earlier one mutated.
/// </summary>
internal static class Fixtures
{
    public static readonly Guid HomeTeamId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AwayTeamId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid MatchId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static Match Match() => new()
    {
        Id = MatchId,
        SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Round = 1,
        KickoffUtc = new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero),
        HomeTeamId = HomeTeamId,
        AwayTeamId = AwayTeamId,
        Status = MatchStatus.Scheduled,
    };

    public static MatchEvent Goal(int sequence, int minute, bool forHome) =>
        Event(sequence, minute, MatchEventKind.Goal, forHome);

    public static MatchEvent Event(int sequence, int minute, MatchEventKind kind, bool forHome) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = MatchId,
        Provider = "simulator",
        ProviderEventId = $"evt-{sequence}",
        Sequence = sequence,
        Minute = minute,
        Kind = kind,
        TeamId = forHome ? HomeTeamId : AwayTeamId,
        OccurredAt = new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero).AddMinutes(minute),
        ReceivedAt = new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero).AddMinutes(minute),
    };

    public static Standing Standing() => new()
    {
        SeasonId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        TeamId = HomeTeamId,
    };
}
