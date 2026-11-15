using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meshsmith.App.Tests;

/// <summary>Meshsmith ships no telemetry, and that is a property of the binaries rather than a
/// promise in a document. This reads the metadata of the three assemblies the app installs and
/// fails if any of them ever references <c>System.Net</c> - directly as a type, or indirectly as
/// an assembly reference, which is how a socket arriving through a package would show up.</summary>
public class NoNetworkTests
{
    private static readonly string[] Shipped =
    {
        "Meshsmith.App.dll", "Meshsmith.Core.dll", "Meshsmith.Worker.dll"
    };

    [Fact]
    public void The_shipped_assemblies_reference_no_network_namespace()
    {
        var offenders = new List<string>();
        int referencesSeen = 0;      // proves the reader really walked the metadata

        foreach (var name in Shipped)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            Assert.True(File.Exists(path), $"{name} is not beside the tests: {path}");

            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();

            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                var referenced = metadata.GetString(reference.Name);
                referencesSeen++;
                if (referenced.StartsWith("System.Net", StringComparison.Ordinal))
                    offenders.Add($"{name}: assembly reference {referenced}");
            }

            foreach (var handle in metadata.TypeReferences)
            {
                var reference = metadata.GetTypeReference(handle);
                if (reference.Namespace.IsNil) continue;

                var space = metadata.GetString(reference.Namespace);
                if (space.StartsWith("System.Net", StringComparison.Ordinal))
                    offenders.Add($"{name}: type {space}.{metadata.GetString(reference.Name)}");
            }
        }

        Assert.True(offenders.Count == 0,
                    "a shipped assembly references the network: " + string.Join(", ", offenders));

        // A metadata walk that finds no references at all would pass this test vacuously, so the
        // walk itself is asserted: App+Core+Worker carry ~39 assembly references today.
        Assert.True(referencesSeen > 10,
                    $"the metadata walk saw only {referencesSeen} assembly references - the test is not looking at the real binaries");
    }
}
