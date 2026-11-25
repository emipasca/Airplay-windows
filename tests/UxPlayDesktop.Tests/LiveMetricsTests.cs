using System;
using UxPlayDesktop.Models;
using UxPlayDesktop.ViewModels;
using Xunit;

namespace UxPlayDesktop.Tests;

public class LiveMetricsTests
{
    private const string StreamLine =
        "METRICS stream fps=59.8 kbps=24500 keyframes=1 net_latency_ms=21.4 net_latency_max_ms=38.0 codec=h265";

    [Fact]
    public void Parser_ReadsStreamLine()
    {
        Assert.True(StreamMetricsParser.TryParseStream(StreamLine, out var s));
        Assert.Equal(59.8, s.Fps, 3);
        Assert.Equal(24500, s.Kbps, 3);
        Assert.Equal(1, s.Keyframes);
        Assert.Equal(21.4, s.NetLatencyMs!.Value, 3);
        Assert.Equal(38.0, s.NetLatencyMaxMs!.Value, 3);
        Assert.Equal("h265", s.Codec);
    }

    [Fact]
    public void Parser_ComputesJitterFromMinAndMax()
    {
        Assert.True(StreamMetricsParser.TryParseStream(
            "METRICS stream fps=60.0 kbps=20000 keyframes=0 net_latency_ms=22.0 net_latency_max_ms=48.5 net_latency_min_ms=12.5 codec=h265", out var s));
        Assert.Equal(12.5, s.NetLatencyMinMs!.Value, 3);
        Assert.Equal(36.0, s.JitterMs!.Value, 3);

        // older engine without min -> jitter unknown
        Assert.True(StreamMetricsParser.TryParseStream(StreamLine, out var old));
        Assert.Null(old.JitterMs);
    }

    [Fact]
    public void Parser_RelativeLatency_KeepsNegativeValues_AndEstimateExcludesNetwork()
    {
        const string rel = "METRICS stream fps=60.0 kbps=8000 keyframes=0 net_latency_ms=1.5 net_latency_max_ms=14.0 net_latency_min_ms=-6.0 codec=h265 latency_ref=rel";
        Assert.True(StreamMetricsParser.TryParseStream(rel, out var s));
        Assert.Equal(LatencyReference.Relative, s.LatencyRef);
        Assert.Equal(-6.0, s.NetLatencyMinMs!.Value, 3);
        Assert.Equal(20.0, s.JitterMs!.Value, 3);

        var vm = new LiveMetricsViewModel();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        vm.ApplyLine("METRICS render rendered=10 dropped=0 pipeline_latency_ms=35.0", t0);
        vm.ApplyLine(rel, t0);
        Assert.True(vm.IsRelativeLatency);
        Assert.Equal("+14 ms", vm.NetLatencyText);
        Assert.Equal(35.0 + 1000.0 / 60.0, vm.EstimatedTotalLatencyMs!.Value, 2);

        Assert.True(StreamMetricsParser.TryParseStream(
            "METRICS stream fps=0.5 kbps=10 keyframes=0 net_latency_ms=-1.0 net_latency_max_ms=-1.0 net_latency_min_ms=-1.0 codec=h265 latency_ref=none", out var none));
        Assert.Null(none.NetLatencyMs);
        Assert.Null(none.JitterMs);
    }

    [Fact]
    public void CsvLog_WritesHeaderAndOneRowPerSample()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airplay-metrics-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var log = new UxPlayDesktop.Services.MetricsCsvLog(dir);
            var vm = new LiveMetricsViewModel();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            log.StartSession(t0);
            vm.ApplyLine("METRICS stream fps=60.0 kbps=20000 keyframes=0 net_latency_ms=22.0 net_latency_max_ms=48.5 net_latency_min_ms=12.5 codec=h265", t0);
            log.Append(vm, t0);
            log.EndSession();

            var lines = System.IO.File.ReadAllLines(log.CurrentPath!);
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("time,fps,mbps", lines[0]);
            Assert.StartsWith("12:00:00,60.0,20.0,22.0,12.5,48.5,36.0,", lines[1]);
        }
        finally
        {
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Parser_TreatsMinusOneAsUnavailable()
    {
        Assert.True(StreamMetricsParser.TryParseStream(
            "METRICS stream fps=10.0 kbps=900 keyframes=0 net_latency_ms=-1.0 net_latency_max_ms=-1.0 codec=h264", out var s));
        Assert.Null(s.NetLatencyMs);
        Assert.Null(s.NetLatencyMaxMs);
    }

    [Fact]
    public void Parser_ReadsRenderLine_AndRejectsOtherLines()
    {
        Assert.True(StreamMetricsParser.TryParseRender("METRICS render rendered=1200 dropped=3 pipeline_latency_ms=4.5", out var r));
        Assert.Equal(1200UL, r.Rendered);
        Assert.Equal(3UL, r.Dropped);
        Assert.Equal(4.5, r.PipelineLatencyMs!.Value, 3);

        Assert.False(StreamMetricsParser.TryParseRender(StreamLine, out _));
        Assert.False(StreamMetricsParser.TryParseStream("Begin streaming to GStreamer video pipeline", out _));
        Assert.False(StreamMetricsParser.IsMetricsLine("raop_rtp_mirror starting mirroring"));
        Assert.True(StreamMetricsParser.IsMetricsLine(StreamLine));
    }

    [Fact]
    public void ViewModel_ComputesRatesAndTotalLatency()
    {
        var vm = new LiveMetricsViewModel();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

        vm.ApplyLine("METRICS render rendered=100 dropped=0 pipeline_latency_ms=5.0", t0);
        vm.ApplyLine(StreamLine, t0.AddSeconds(1));
        vm.ApplyLine("METRICS render rendered=160 dropped=2 pipeline_latency_ms=5.0", t0.AddSeconds(1));

        Assert.False(vm.IsIdle);
        Assert.Equal(59.8, vm.Fps, 3);
        Assert.Equal(24.5, vm.BitrateMbps, 3);
        Assert.Equal(60.0, vm.RenderFps, 3);
        Assert.Equal(2.0, vm.DroppedPerSecond, 3);
        // 21.4 network + 5.0 pipeline + one 60 Hz frame (16.67)
        Assert.Equal(43.07, vm.EstimatedTotalLatencyMs!.Value, 2);
        Assert.Equal("Fair", vm.HealthText);
    }

    [Fact]
    public void ViewModel_GoesIdleWhenScreenIsStatic_AndResets()
    {
        var vm = new LiveMetricsViewModel();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        vm.ApplyLine(StreamLine, t0);
        Assert.Equal("Excellent", vm.HealthText);

        vm.Refresh(t0 + LiveMetricsViewModel.StaleAfter + TimeSpan.FromSeconds(1));
        Assert.True(vm.IsIdle);
        Assert.Equal(0, vm.Fps);
        Assert.Equal("Idle", vm.FpsText);

        vm.Reset();
        Assert.False(vm.HasData);
        Assert.Equal("—", vm.FpsText);
    }

    [Fact]
    public void ViewModel_HandlesRenderCounterReset()
    {
        var vm = new LiveMetricsViewModel();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        vm.ApplyLine("METRICS render rendered=5000 dropped=10 pipeline_latency_ms=5.0", t0);
        // pipeline rebuilt (e.g. codec change): counters restart from 0 -> no negative/huge rates
        vm.ApplyLine("METRICS render rendered=30 dropped=0 pipeline_latency_ms=5.0", t0.AddSeconds(1));
        Assert.True(vm.RenderFps >= 0 && vm.RenderFps < 1000);
        Assert.True(vm.DroppedPerSecond >= 0);
    }
}
