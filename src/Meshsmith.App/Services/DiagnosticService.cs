using System.IO;

namespace Meshsmith.App.Services;

/// <summary>Writes the bundle that makes a failure reproducible. Implementations must never throw.</summary>
public interface IDiagnosticService
{
    string? Capture(DiagnosticInputs inputs);
}

public sealed class DiagnosticService : IDiagnosticService
{
    private readonly string _logPath;
    private readonly long _maxModelBytes;

    public DiagnosticService(string logPath, long maxModelBytes = DiagnosticBundle.DefaultMaxModelBytes)
    {
        _logPath = logPath;
        _maxModelBytes = maxModelBytes;
    }

    public string? Capture(DiagnosticInputs inputs)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(),
                $"meshsmith-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            DiagnosticBundle.Create(path, inputs, _logPath, _maxModelBytes);
            return path;
        }
        catch (Exception)
        {
            // A bundle that cannot be written must never replace the failure it was meant to describe.
            return null;
        }
    }
}
