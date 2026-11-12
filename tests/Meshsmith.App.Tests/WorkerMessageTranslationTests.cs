using Meshsmith.App.Services;
using Meshsmith.Core.Ipc;
using Xunit;

namespace Meshsmith.App.Tests;

public class WorkerMessageTranslationTests
{
    [Fact]
    public void A_progress_message_becomes_a_progress_report()
    {
        var report = WorkerConversionService.Translate(new WorkerMessage
        {
            Type = WorkerMessage.Progress,
            Stage = "sewing",
            Fraction = 0.75
        });

        Assert.NotNull(report);
        Assert.False(report!.Value.IsBeat);
        Assert.Equal("sewing", report.Value.Stage);
        Assert.Equal(75, report.Value.Fraction * 100, 3);
    }

    [Fact]
    public void A_beat_becomes_a_beat_report_with_the_cpu_share()
    {
        var report = WorkerConversionService.Translate(new WorkerMessage
        {
            Type = WorkerMessage.Beat,
            Cpu = 97
        });

        Assert.NotNull(report);
        Assert.True(report!.Value.IsBeat);
        Assert.Equal(97, report.Value.CpuPercent);
    }

    [Fact]
    public void A_result_is_not_a_progress_report()
    {
        Assert.Null(WorkerConversionService.Translate(new WorkerMessage { Type = WorkerMessage.Result }));
        Assert.Null(WorkerConversionService.Translate(new WorkerMessage { Type = WorkerMessage.Error }));
    }
}
