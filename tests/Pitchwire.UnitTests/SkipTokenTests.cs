using Pitchwire.Application.Paging;

namespace Pitchwire.UnitTests;

/// <summary>
/// What a continuation token accepts and what it refuses.
/// </summary>
/// <remarks>
/// A token that decodes into the wrong shape, or falls back to the first page when it cannot be read,
/// produces duplicated rows in a client that is paging through a list and has no way to notice.
/// </remarks>
public sealed class SkipTokenTests
{
    private static readonly MatchCursor Cursor =
        new(new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero), Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void A_token_survives_the_round_trip()
    {
        var token = SkipToken.Encode("fixtures", Cursor);

        SkipToken.TryDecode<MatchCursor>(token, "fixtures", out var decoded).ShouldBeTrue();
        decoded.ShouldBe(Cursor);
    }

    [Fact]
    public void A_token_is_safe_to_put_in_a_query_string_unescaped()
    {
        var token = SkipToken.Encode("fixtures", Cursor);

        token.ShouldNotContain("+");
        token.ShouldNotContain("/");
        token.ShouldNotContain("=");
    }

    [Fact]
    public void A_token_from_one_list_is_refused_by_another()
    {
        // Without the kind inside the token, a fixture cursor handed to the results list would decode
        // cleanly and silently place the reader somewhere nobody asked for.
        var token = SkipToken.Encode("fixtures", Cursor);

        SkipToken.TryDecode<MatchCursor>(token, "results", out _).ShouldBeFalse();
    }

    [Fact]
    public void Nonsense_is_refused_rather_than_treated_as_the_beginning()
    {
        SkipToken.TryDecode<MatchCursor>("not a token", "fixtures", out _).ShouldBeFalse();
        SkipToken.TryDecode<MatchCursor>("", "fixtures", out _).ShouldBeFalse();
        SkipToken.TryDecode<MatchCursor>(null, "fixtures", out _).ShouldBeFalse();
    }

    [Fact]
    public void Base64_of_something_else_entirely_is_refused()
    {
        var notATokenAtAll = Convert.ToBase64String("hello there"u8.ToArray()).TrimEnd('=');

        SkipToken.TryDecode<MatchCursor>(notATokenAtAll, "fixtures", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_page_size_is_defaulted_and_capped()
    {
        PageSize.Clamp(null).ShouldBe(PageSize.Default);
        PageSize.Clamp(0).ShouldBe(PageSize.Default);
        PageSize.Clamp(-10).ShouldBe(PageSize.Default);
        PageSize.Clamp(25).ShouldBe(25);
        PageSize.Clamp(5_000).ShouldBe(PageSize.Max);
    }
}
