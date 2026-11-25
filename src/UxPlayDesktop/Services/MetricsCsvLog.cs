using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UxPlayDesktop.ViewModels;

namespace UxPlayDesktop.Services;

/// <summary>
/// Writes one CSV row per second of streaming to
/// %APPDATA%\UxPlayDesktop\metrics\session-yyyyMMdd-HHmmss.csv (newest 20 sessions kept),
/// so stutter and latency can be analysed after the fact.
/// </summary>
public sealed class MetricsCsvLog : IDisposable
{
    private const int KeepSessions = 20;
    private const string Header =
        "time,fps,mbps,net_latency_avg_ms,net_latency_min_ms,net_latency_max_ms,jitter_ms," +
        "shown_fps,dropped_per_s,dropped_total,pipeline_latency_ms,total_latency_est_ms," +
        "gpu_decode_pct,gpu_3d_pct,engine_cpu_pct,engine_ram_mb,latency_ref,engine_metrics_cost_ms";

    private readonly string _folder;
    private StreamWriter? _writer;

    public string? CurrentPath { get; private set; }

    public MetricsCsvLog(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UxPlayDesktop", "metrics");
    }

    public void StartSession(DateTime now)
    {
        EndSession();
        try
        {
            Directory.CreateDirectory(_folder);
            Prune();
            CurrentPath = Path.Combine(_folder, $"session-{now:yyyyMMdd-HHmmss}.csv");
            _writer = new StreamWriter(CurrentPath, append: false) { AutoFlush = true };
            _writer.WriteLine(Header);
        }
        catch
        {
            _writer = null;   // logging is best-effort; never break streaming
        }
    }

    public bool IsSessionOpen => _writer != null;

    public void Append(LiveMetricsViewModel m, DateTime now)
    {
        if (_writer == null) return;
        try
        {
            var r = m.Resources;
            _writer.WriteLine(string.Join(",",
                now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                N(m.Fps), N(m.BitrateMbps),
                N(m.NetLatencyMs), N(m.NetLatencyMinMs), N(m.NetLatencyMaxMs), N(m.JitterMs),
                N(m.RenderFps), N(m.DroppedPerSecond), m.DroppedTotal.ToString(CultureInfo.InvariantCulture),
                N(m.PipelineLatencyMs), N(m.EstimatedTotalLatencyMs),
                N(r?.GpuDecodePercent), N(r?.Gpu3dPercent), N(r?.CpuPercent), N(r?.RamMb),
                m.IsRelativeLatency ? "rel" : (m.NetLatencyMs.HasValue ? "abs" : ""),
                N(m.EngineMetricsCostMs)));
        }
        catch { }
    }

    public void EndSession()
    {
        try { _writer?.Dispose(); } catch { }
        _writer = null;
    }

    private void Prune()
    {
        foreach (var old in new DirectoryInfo(_folder).GetFiles("session-*.csv")
                     .OrderByDescending(f => f.Name).Skip(KeepSessions - 1))
        {
            try { old.Delete(); } catch { }
        }
    }

    private static string N(double? v) => v is double d ? d.ToString("0.0", CultureInfo.InvariantCulture) : "";

    public void Dispose() => EndSession();
}
