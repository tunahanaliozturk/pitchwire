using Pitchwire.Feed;

namespace Pitchwire.UnitTests;

/// <summary>
/// The generator the simulator leans on for every decision it makes.
/// </summary>
/// <remarks>
/// A skewed generator would quietly turn the misbehaviour rates into decoration: the configuration
/// would say ten percent of events are dropped and none would be, and every test built on top would
/// pass while exercising the happy path.
/// </remarks>
public sealed class RollsTests
{
    [Fact]
    public void Chance_fires_at_about_the_rate_it_is_given()
    {
        var rolls = new Rolls(20_260_920);
        var hits = 0;

        for (var roll = 0; roll < 10_000; roll++)
        {
            if (rolls.Chance(0.10))
            {
                hits++;
            }
        }

        hits.ShouldBeInRange(850, 1150);
    }

    [Fact]
    public void Next_spreads_across_the_range_it_is_bounded_by()
    {
        var rolls = new Rolls(7);
        var buckets = new int[10];

        for (var roll = 0; roll < 10_000; roll++)
        {
            buckets[rolls.Next(10)]++;
        }

        buckets.ShouldAllBe(count => count > 800 && count < 1200);
    }
}
