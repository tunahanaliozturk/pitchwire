using System.Text;
using Pitchwire.Contracts;

namespace Pitchwire.UnitTests;

/// <summary>
/// What the ingestion boundary accepts as proof that a batch came from the provider.
/// </summary>
/// <remarks>
/// Signature verification is a place where a mistake looks like working code. An early return on a
/// length mismatch, a comparison that comes back on the first differing byte, or a timestamp that sits
/// in a header without being signed all produce a function that accepts every real request and a
/// forged one too. These pin down the cases that separate the two.
/// </remarks>
public sealed class RequestSignerTests
{
    private const string Secret = "a-secret-that-is-long-enough-to-be-real";
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 18, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"events":[]}""");

    [Fact]
    public void Verify_accepts_a_signature_it_produced()
    {
        var signature = RequestSigner.Sign(Secret, Now, Body);

        var verdict = RequestSigner.Verify(Secret, signature, Stamp(Now), Body, Now, Tolerance);

        verdict.ShouldBe(SignatureVerdict.Valid);
    }

    [Fact]
    public void Verify_rejects_a_body_changed_after_signing()
    {
        var signature = RequestSigner.Sign(Secret, Now, Body);
        var tampered = Encoding.UTF8.GetBytes("""{"events":[{"kind":"Goal"}]}""");

        var verdict = RequestSigner.Verify(Secret, signature, Stamp(Now), tampered, Now, Tolerance);

        verdict.ShouldBe(SignatureVerdict.Mismatch);
    }

    [Fact]
    public void Verify_rejects_a_signature_made_with_another_secret()
    {
        var signature = RequestSigner.Sign("a-different-secret-entirely", Now, Body);

        var verdict = RequestSigner.Verify(Secret, signature, Stamp(Now), Body, Now, Tolerance);

        verdict.ShouldBe(SignatureVerdict.Mismatch);
    }

    [Fact]
    public void Verify_rejects_a_replay_older_than_the_tolerance()
    {
        var signature = RequestSigner.Sign(Secret, Now, Body);

        var verdict = RequestSigner.Verify(Secret, signature, Stamp(Now), Body, Now.AddMinutes(6), Tolerance);

        verdict.ShouldBe(SignatureVerdict.Expired);
    }

    [Fact]
    public void Verify_rejects_a_timestamp_far_in_the_future()
    {
        // Clock skew cuts both ways, and a request stamped well ahead of us is as suspicious as a
        // stale one. Accepting it would let a captured batch be held and replayed at a chosen moment.
        var future = Now.AddMinutes(6);
        var signature = RequestSigner.Sign(Secret, future, Body);

        var verdict = RequestSigner.Verify(Secret, signature, Stamp(future), Body, Now, Tolerance);

        verdict.ShouldBe(SignatureVerdict.Expired);
    }

    [Fact]
    public void Verify_accepts_a_request_at_the_edge_of_the_tolerance()
    {
        var signature = RequestSigner.Sign(Secret, Now, Body);

        var verdict = RequestSigner.Verify(Secret, signature, Stamp(Now), Body, Now.Add(Tolerance), Tolerance);

        verdict.ShouldBe(SignatureVerdict.Valid);
    }

    [Fact]
    public void Verify_separates_a_missing_header_from_a_malformed_one()
    {
        RequestSigner.Verify(Secret, null, Stamp(Now), Body, Now, Tolerance)
            .ShouldBe(SignatureVerdict.Missing);

        RequestSigner.Verify(Secret, RequestSigner.Sign(Secret, Now, Body), null, Body, Now, Tolerance)
            .ShouldBe(SignatureVerdict.Missing);

        RequestSigner.Verify(Secret, "not base64 at all", Stamp(Now), Body, Now, Tolerance)
            .ShouldBe(SignatureVerdict.Malformed);

        RequestSigner.Verify(Secret, RequestSigner.Sign(Secret, Now, Body), "yesterday", Body, Now, Tolerance)
            .ShouldBe(SignatureVerdict.Malformed);
    }

    [Fact]
    public void Verify_rejects_a_signature_of_the_wrong_length()
    {
        // A truncated signature must not be compared against a truncated expectation. Verifying only
        // the bytes that were supplied would accept a one byte signature once in 256 attempts.
        var truncated = Convert.ToBase64String(Convert.FromBase64String(RequestSigner.Sign(Secret, Now, Body))[..16]);

        var verdict = RequestSigner.Verify(Secret, truncated, Stamp(Now), Body, Now, Tolerance);

        verdict.ShouldBe(SignatureVerdict.Malformed);
    }

    [Fact]
    public void Sign_binds_the_signature_to_the_timestamp()
    {
        // The timestamp is part of the signed material. Were it only a header, a captured body and
        // signature could be moved into a later window by rewriting one number.
        var first = RequestSigner.Sign(Secret, Now, Body);
        var second = RequestSigner.Sign(Secret, Now.AddSeconds(1), Body);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Sign_produces_a_different_signature_for_a_body_that_only_moved_its_boundary()
    {
        // Concatenating the timestamp and the body without a length prefix lets two different inputs
        // produce one signed string. These two must not collide.
        var first = RequestSigner.Sign(Secret, Now, Encoding.UTF8.GetBytes("12abc"));
        var second = RequestSigner.Sign(Secret, Now, Encoding.UTF8.GetBytes("2abc"));

        first.ShouldNotBe(second);
    }

    private static string Stamp(DateTimeOffset moment) =>
        moment.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
}
