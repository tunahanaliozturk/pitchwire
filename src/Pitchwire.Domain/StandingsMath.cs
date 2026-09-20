
namespace Pitchwire.Domain;

/// <summary>
/// What a finished match does to two league table rows.
/// </summary>
/// <remarks>
/// Separate from the reducer because it runs once per match rather than once per event, and separate
/// from the database because the arithmetic is the part worth pinning down in a test.
/// </remarks>
public static class StandingsMath
{
    private const int PointsForWin = 3;
    private const int PointsForDraw = 1;

    public static void ApplyFinishedMatch(Standing home, Standing away, int homeScore, int awayScore)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(away);
        ArgumentOutOfRangeException.ThrowIfNegative(homeScore);
        ArgumentOutOfRangeException.ThrowIfNegative(awayScore);

        home.Played++;
        away.Played++;

        home.GoalsFor += homeScore;
        home.GoalsAgainst += awayScore;
        away.GoalsFor += awayScore;
        away.GoalsAgainst += homeScore;

        if (homeScore > awayScore)
        {
            Win(home);
            Loss(away);
        }
        else if (awayScore > homeScore)
        {
            Win(away);
            Loss(home);
        }
        else
        {
            Draw(home);
            Draw(away);
        }
    }

    private static void Win(Standing standing)
    {
        standing.Won++;
        standing.Points += PointsForWin;
    }

    private static void Draw(Standing standing)
    {
        standing.Drawn++;
        standing.Points += PointsForDraw;
    }

    private static void Loss(Standing standing) => standing.Lost++;
}
