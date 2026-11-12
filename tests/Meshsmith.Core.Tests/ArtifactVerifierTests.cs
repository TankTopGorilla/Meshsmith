using Meshsmith.Core.Export;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

public class ArtifactVerifierTests
{
    private static ConversionResult ConvertCube() =>
        ConversionPipeline.Run(StlFixtures.WriteCube(10),
            new ConversionOptions { KeepTemporaryFiles = true });

    [Fact]
    public void Step_written_from_a_cube_verifies_against_its_own_numbers()
    {
        var result = ConvertCube();

        var check = ArtifactVerifier.Verify(result.StepPath, result.FaceCount, result.SolidCount, result.Volume);

        Assert.True(check.Ok, check.Detail);
        Assert.Equal(6, check.Faces);
        Assert.Equal(1, check.Solids);
        Assert.Equal(1000.0, check.Volume, 0.5);
        Assert.True(check.IsValid);
        Assert.Equal(0.0, check.VolumeDeltaPercent, 0.01);
    }

    [Fact]
    public void Iges_written_from_a_cube_also_verifies()
    {
        var result = ConvertCube();

        var check = ArtifactVerifier.Verify(result.IgesPath, result.FaceCount, result.SolidCount, result.Volume);

        Assert.True(check.Ok, check.Detail);
        Assert.Equal(6, check.Faces);
        Assert.Equal(1000.0, check.Volume, 0.5);
    }

    [Fact]
    public void A_face_count_mismatch_is_reported_not_hidden()
    {
        var result = ConvertCube();

        var check = ArtifactVerifier.Verify(result.StepPath, result.FaceCount + 1,
                                           result.SolidCount, result.Volume);

        Assert.False(check.Ok);
        Assert.Contains("faces", check.Detail);
    }

    [Fact]
    public void A_solid_regrouping_is_reported_but_does_not_fail_the_file()
    {
        var result = ConvertCube();

        var check = ArtifactVerifier.Verify(result.StepPath, result.FaceCount,
                                           expectedSolids: 7, result.Volume);

        Assert.True(check.Ok, check.Detail);
        Assert.Contains("regrouped", check.Detail);
    }

    [Fact]
    public void A_body_above_the_face_limit_is_reported_as_skipped()
    {
        var result = ConversionPipeline.Run(StlFixtures.WriteCube(10),
            new ConversionOptions { KeepTemporaryFiles = true, VerifyFaceLimit = 1 });

        Assert.Equal(ArtifactStatus.Skipped, result.Artifact.Status);
        Assert.Contains("limit", result.Artifact.Detail);
        Assert.False(result.Artifact.Ok);
    }

    [Fact]
    public void A_file_that_is_not_cad_is_reported_not_thrown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"not-cad-{Guid.NewGuid():N}.step");
        File.WriteAllText(path, "this is not a step file");
        try
        {
            var check = ArtifactVerifier.Verify(path, 6, 1, 1000);

            Assert.False(check.Ok);
            Assert.False(string.IsNullOrWhiteSpace(check.Detail));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
