namespace Pitchwire.Domain;

/// <summary>
/// Where a player lined up, which decides which parts of the rating apply to them.
/// </summary>
public enum Position
{
    Goalkeeper,
    Defender,
    Midfielder,
    Forward,
}

/// <summary>
/// Everything one player did in one match, as the event log records it.
/// </summary>
public sealed record PlayerMatchLine(
    Guid PlayerId,
    Position Position,
    int MinutesPlayed,
    int Goals,
    int PenaltyGoals,
    int OwnGoals,
    int Assists,
    int YellowCards,
    int RedCards,
    int TeamGoalsFor,
    int TeamGoalsAgainst);

/// <summary>
/// A match rating derived from what the event log says a player did.
/// </summary>
/// <remarks>
/// This is a model, and a deliberately simple one, rather than a scout's opinion. Every point in it
/// comes from something that happened on the pitch and is recorded in the log, so the same match
/// always produces the same ratings and anybody can check why a player got the number they did.
/// Commercial ratings weigh hundreds of on the ball actions this service never sees, and pretending
/// otherwise would be inventing numbers with a decimal point to make them look measured.
/// <para>
/// A player on the pitch for less than twenty minutes is not rated. A cameo in stoppage time has
/// nothing in it to judge, and a 6.0 for standing near the halfway line would say something it
/// cannot know.
/// </para>
/// </remarks>
public static class PlayerRating
{
    public const decimal Baseline = 6.0m;
    public const decimal Floor = 3.0m;
    public const decimal Ceiling = 10.0m;
    public const int MinimumMinutes = 20;

    public static decimal? Calculate(PlayerMatchLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.MinutesPlayed < MinimumMinutes)
        {
            return null;
        }

        var rating = Baseline;

        // A goal from open play is worth more than a penalty, which is a finish from twelve yards
        // with nobody allowed within ten of the taker.
        rating += 1.2m * (line.Goals - line.PenaltyGoals);
        rating += 1.0m * line.PenaltyGoals;
        rating += 0.7m * line.Assists;
        rating -= 1.0m * line.OwnGoals;
        rating -= 0.4m * line.YellowCards;
        rating -= 2.0m * line.RedCards;

        rating += line.TeamGoalsFor.CompareTo(line.TeamGoalsAgainst) switch
        {
            > 0 => 0.3m,
            < 0 => -0.3m,
            _ => 0m,
        };

        if (line.Position is Position.Goalkeeper or Position.Defender)
        {
            // Defending is the part of the game an event log sees least of, so it is judged by the
            // one outcome it does record: what got past them.
            rating -= 0.2m * line.TeamGoalsAgainst;

            if (line.TeamGoalsAgainst == 0 && line.MinutesPlayed >= 60)
            {
                rating += 0.6m;
            }
        }

        return Math.Round(Math.Clamp(rating, Floor, Ceiling), 1, MidpointRounding.AwayFromZero);
    }
}
