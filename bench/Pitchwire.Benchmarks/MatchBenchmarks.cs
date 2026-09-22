using BenchmarkDotNet.Attributes;
using Pitchwire.Application.Reads;
using Pitchwire.Domain;

namespace Pitchwire.Benchmarks;

/// <summary>
/// The work a match page does on every request that is not a database round trip.
/// </summary>
/// <remarks>
/// Rebuilding a match from its log is the price of the ordering guarantee: an event that arrives out
/// of order is not patched into the running total, the total is worked out again from the beginning.
/// That is only a defensible design if the beginning is cheap, so this measures it, and it measures
/// the sheet assembly on top of it, because the two together are what a reader waits for.
/// </remarks>
[MemoryDiagnoser]
public class MatchBenchmarks
{
    private static readonly Guid HomeTeam = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AwayTeam = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private MatchEvent[] events = [];
    private MatchLineupEntry[] lineup = [];
    private MatchTeamSheet[] sheets = [];
    private Dictionary<Guid, string> names = [];
    private Match match = null!;

    /// <summary>
    /// A quiet match, a busy one, and one that could only happen on the last day of a season.
    /// </summary>
    [Params(20, 60, 200)]
    public int Events { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var players = Enumerable.Range(0, 36)
            .Select(index => (Id: Guid.Parse($"00000000-0000-0000-0000-{index:D12}"), Index: index))
            .ToArray();

        names = players.ToDictionary(player => player.Id, player => $"Player {player.Index}");

        lineup =
        [
            .. players.Select(player => new MatchLineupEntry
            {
                TeamId = player.Index < 18 ? HomeTeam : AwayTeam,
                PlayerId = player.Id,
                ShirtNumber = (player.Index % 18) + 1,
                Position = (Position)(player.Index % 4),
                IsStarter = player.Index % 18 < 11,
            }),
        ];

        sheets =
        [
            new MatchTeamSheet { TeamId = HomeTeam, Formation = "4-3-3" },
            new MatchTeamSheet { TeamId = AwayTeam, Formation = "4-4-2" },
        ];

        events =
        [
            .. Enumerable.Range(1, Events).Select(sequence => new MatchEvent
            {
                MatchId = Guid.Empty,
                Provider = "bench",
                ProviderEventId = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Sequence = sequence,
                Minute = Math.Min(94, sequence * 94 / Math.Max(1, Events)),
                Kind = Kind(sequence),
                TeamId = sequence % 2 == 0 ? HomeTeam : AwayTeam,
                PlayerId = players[sequence % 36].Id,
                AssistPlayerId = sequence % 5 == 0 ? players[(sequence + 3) % 36].Id : null,
                ReplacedPlayerId = Kind(sequence) == MatchEventKind.Substitution
                    ? players[(sequence + 7) % 36].Id
                    : null,
            }),
        ];

        match = NewMatch();
    }

    /// <summary>
    /// The score, minute and status, worked out again from the whole log.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int RebuildFromLog()
    {
        var rebuilt = NewMatch();

        foreach (var @event in events)
        {
            MatchStateReducer.Apply(rebuilt, @event);
        }

        return rebuilt.HomeScore + rebuilt.AwayScore;
    }

    /// <summary>
    /// The team sheets a reader sees, including the minutes and the ratings derived from the log.
    /// </summary>
    [Benchmark]
    public int BuildTeamSheets() =>
        MatchSheetAssembly.Build(match, sheets, lineup, events, names).Count;

    /// <summary>
    /// Minutes alone, which the sheet assembly calls once and the rating depends on entirely.
    /// </summary>
    [Benchmark]
    public int MinutesPlayed() => Participation.MinutesPlayed(lineup, events, 94).Count;

    private static MatchEventKind Kind(int sequence) => (sequence % 7) switch
    {
        0 => MatchEventKind.Goal,
        1 => MatchEventKind.Yellow,
        2 => MatchEventKind.Substitution,
        3 => MatchEventKind.PenaltyGoal,
        4 => MatchEventKind.OwnGoal,
        5 => MatchEventKind.Red,
        _ => MatchEventKind.Substitution,
    };

    private static Match NewMatch() => new()
    {
        Id = Guid.Empty,
        HomeTeamId = HomeTeam,
        AwayTeamId = AwayTeam,
        Minute = 94,
        Status = MatchStatus.Live,
    };
}
