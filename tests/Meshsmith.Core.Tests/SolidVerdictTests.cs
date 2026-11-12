using Meshsmith.Core.Export;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

public class SolidVerdictTests
{
    private static ArtifactCheck Verified => new(ArtifactStatus.Verified, 12, 1, 1000, 0, true, "ok");
    private static ArtifactCheck Mismatch => new(ArtifactStatus.Mismatch, 11, 1, 900, 10, false, "faces 11 but the body had 12");
    private static ArtifactCheck Skipped => ArtifactCheck.Skipped("12 765 faces is above the 4 000-face verification limit");

    [Fact]
    public void A_valid_body_with_a_verified_file_is_valid()
    {
        var (valid, note) = SolidVerdict.Decide(bodyValid: true, Verified);

        Assert.True(valid);
        Assert.Null(note);
    }

    [Fact]
    public void A_valid_body_with_a_mismatched_file_is_not_valid()
    {
        var (valid, _) = SolidVerdict.Decide(bodyValid: true, Mismatch);

        Assert.False(valid);
    }

    [Fact]
    public void A_valid_body_with_an_unchecked_file_is_valid()
    {
        var (valid, note) = SolidVerdict.Decide(bodyValid: true, Skipped);

        Assert.True(valid);
        Assert.Null(note);
    }

    [Fact]
    public void An_invalid_body_with_a_verified_file_is_valid_and_says_so()
    {
        var (valid, note) = SolidVerdict.Decide(bodyValid: false, Verified);

        Assert.True(valid);
        Assert.NotNull(note);
        Assert.Contains("in-memory", note);
        Assert.Contains("reads back valid", note);
    }

    [Fact]
    public void An_invalid_body_with_a_mismatched_file_is_not_valid()
    {
        var (valid, _) = SolidVerdict.Decide(bodyValid: false, Mismatch);

        Assert.False(valid);
    }

    [Fact]
    public void An_invalid_body_with_an_unchecked_file_is_not_valid()
    {
        var (valid, _) = SolidVerdict.Decide(bodyValid: false, Skipped);

        Assert.False(valid);
    }
}
