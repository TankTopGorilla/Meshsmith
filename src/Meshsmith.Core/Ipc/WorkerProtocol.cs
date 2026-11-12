using System.Text.Json;
using System.Text.Json.Serialization;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.Core.Ipc;

/// <summary>One JSON object per line over the named pipe shared by the app and
/// <c>Meshsmith.Worker.exe</c>. A pipe rather than stdout: the OCCT exporters write their own
/// statistics to stdout, and a shared stream loses a message to one interleaved write.</summary>
public sealed class WorkerMessage
{
    public const string Progress = "progress";
    /// <summary>A liveness beat from the child: the process is alive, and here is what its CPU has
    /// been doing since the previous beat. The parent uses it to tell "slow" from "stuck".</summary>
    public const string Beat = "beat";
    public const string Result = "result";
    public const string Error = "error";
    public const string Cancelled = "cancelled";

    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("fraction")] public double? Fraction { get; set; }
    [JsonPropertyName("cpu")] public double? Cpu { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("result")] public ConversionResult? ResultBody { get; set; }
}

public static class WorkerLine
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Write(WorkerMessage message) => JsonSerializer.Serialize(message, Options);

    /// <summary>A malformed line must not kill a conversion: anything unreadable is dropped.</summary>
    public static WorkerMessage? Read(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkerMessage>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
