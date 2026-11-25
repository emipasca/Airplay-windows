using System;
using System.Globalization;
using UxPlayDesktop.Models;
using UxPlayDesktop.Services;

namespace UxPlayDesktop.ViewModels;

/// <summary>
/// Live stream metrics shown on the dashboard while a client is mirroring.
/// Fed by engine METRICS lines (<see cref="ApplyLine"/>) and by a once-per-second
/// resource sample of the engine process (<see cref="ApplyResources"/>).
/// Not thread-safe: call on the UI thread.
/// </summary>
public class LiveMetricsViewModel : ViewModelBase
{
    /// <summary>AirPlay only sends frames when the screen changes; after this long without
    /// a stream sample the picture is static and fps is reported as idle, not as a fault.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2.5);
    private const double FrameMs = 1000.0 / 60.0;

    private StreamSample? _stream;
    private RenderSample? _render;
    private RenderSample? _prevRender;
    private DateTime _streamAt;
    private DateTime _renderAt;
    private DateTime _prevRenderAt;
    private EngineResourceSample? _resources;
    private double _peakMbps;

    /// <summary>Raised after each once-per-second stream sample (used for the CSV log).</summary>
    public event Action? StreamSampleApplied;
    public EngineResourceSample? Resources => _resources;

    public bool HasData => _stream != null || _render != null;
    public bool IsIdle { get; private set; } = true;

    public double Fps => IsIdle || _stream == null ? 0 : _stream.Fps;
    public double BitrateMbps => IsIdle || _stream == null ? 0 : _stream.Kbps / 1000.0;
    public double? NetLatencyMs => _stream?.NetLatencyMs;
    public double? NetLatencyMaxMs => _stream?.NetLatencyMaxMs;
    public double? NetLatencyMinMs => _stream?.NetLatencyMinMs;
    public double? PipelineLatencyMs => _render?.PipelineLatencyMs;
    /// <summary>Time the engine spent collecting render stats on its receive thread (diagnostic).</summary>
    public double? EngineMetricsCostMs => _render?.MetricsCostMs;
    public double RenderFps { get; private set; }
    public double DroppedPerSecond { get; private set; }
    public ulong DroppedTotal => _render?.Dropped ?? 0;

    /// <summary>Estimated capture-to-screen delay: client encode + network (measured with the
    /// NTP-synced client timestamp), plus GStreamer pipeline latency, plus one display frame.</summary>
    public double? EstimatedTotalLatencyMs =>
        IsRelativeLatency
            ? (PipelineLatencyMs is double p ? p + FrameMs : null)   // network part unknown without clock sync
            : NetLatencyMs is double net ? net + (PipelineLatencyMs ?? 0) + FrameMs : null;

    /// <summary>True when the client never synced clocks, so delays are relative to the first frame.</summary>
    public bool IsRelativeLatency => _stream?.LatencyRef == LatencyReference.Relative;

    // ---- Display strings ----
    public string FpsText => HasData ? (IsIdle ? "Idle" : F1(Fps)) : "—";
    public string FpsSubText => IsIdle && HasData ? "Screen static — no new frames" : $"Shown {F1(RenderFps)}/s • target 60";
    public double? JitterMs => _stream?.JitterMs;
    public string JitterText => JitterMs is double j ? $"{j:0} ms" : "—";
    public string JitterSubText => JitterMs is double j
        ? (j < 10 ? "Steady delivery" : j < 25 ? "Some unevenness" : "Uneven — causes stutter")
        : "Spread of frame delivery";
    public string BitrateText => HasData ? $"{F1(BitrateMbps)} Mbps" : "—";
    public string BitrateSubText => $"Peak {F1(_peakMbps)} Mbps • {(_stream?.Codec ?? "—").ToUpperInvariant()}";
    public string NetLatencyText => IsRelativeLatency
        ? (NetLatencyMaxMs is double late ? $"{late:+0;-0;0} ms" : "—")
        : NetLatencyMs is double v ? $"{v:0} ms" : "—";
    public string NetLatencySubText => IsRelativeLatency
        ? "Latest frame vs schedule (Mac clock not synced)"
        : NetLatencyMaxMs is double m ? $"Worst {m:0} ms (last 1 s)" : "Mac → PC (encode + Wi-Fi)";
    public string TotalLatencyText => EstimatedTotalLatencyMs is double v
        ? (IsRelativeLatency ? $"~{v:0} ms + Wi-Fi" : $"~{v:0} ms")
        : "—";
    public string TotalLatencySubText => PipelineLatencyMs is double p ? $"Decode/render pipeline {p:0} ms" : "Estimate";
    public string DroppedText => _render == null ? "—" : DroppedTotal.ToString("N0", CultureInfo.CurrentCulture);
    public string DroppedSubText => $"{F1(DroppedPerSecond)}/s now • keyframes {_stream?.Keyframes ?? 0}/s";
    public string GpuDecodeText => _resources?.GpuDecodePercent is double g ? $"{g:0}%" : "—";
    public string GpuDecodeSubText => _resources?.Gpu3dPercent is double r ? $"Render (3D) {r:0}%" : "Hardware decoder";
    public string EngineCpuText => _resources == null ? "—" : $"{_resources.CpuPercent:0.0}%";
    public string EngineRamText => _resources == null ? "RAM —" : $"RAM {_resources.RamMb:0} MB";

    /// <summary>One-word verdict for the stream, based on latency and dropped frames.</summary>
    public string HealthText
    {
        get
        {
            if (!HasData) return "Waiting for video";
            if (IsIdle) return "Connected • idle";
            var latency = EstimatedTotalLatencyMs ?? 0;
            if (DroppedPerSecond >= 3 || latency > 150) return "Poor";
            if (DroppedPerSecond >= 0.5 || latency > 80) return "Fair";
            return "Excellent";
        }
    }

    public string HealthColor => HealthText switch
    {
        "Excellent" => "#25D366",
        "Fair" => "#F59E0B",
        "Poor" => "#EF4444",
        _ => "#94A3B8"
    };

    /// <summary>Consumes an engine METRICS line. Returns false if the line is not a metrics line.</summary>
    public bool ApplyLine(string line, DateTime now)
    {
        if (StreamMetricsParser.TryParseStream(line, out var stream))
        {
            _stream = stream;
            _streamAt = now;
            _peakMbps = Math.Max(_peakMbps, stream.Kbps / 1000.0);
            Refresh(now);
            StreamSampleApplied?.Invoke();
            return true;
        }

        if (StreamMetricsParser.TryParseRender(line, out var render))
        {
            if (_render != null && render.Rendered >= _render.Rendered)
            {
                _prevRender = _render;
                _prevRenderAt = _renderAt;
            }
            else
            {
                _prevRender = null;   // first sample, or the pipeline was rebuilt (counters reset)
            }
            _render = render;
            _renderAt = now;

            if (_prevRender != null)
            {
                var secs = (now - _prevRenderAt).TotalSeconds;
                if (secs > 0)
                {
                    RenderFps = (render.Rendered - _prevRender.Rendered) / secs;
                    DroppedPerSecond = (render.Dropped - _prevRender.Dropped) / secs;
                }
            }
            Refresh(now);
            return true;
        }

        return false;
    }

    public void ApplyResources(EngineResourceSample? sample, DateTime now)
    {
        _resources = sample;
        Refresh(now);
    }

    /// <summary>Re-evaluates idleness (call once per second even when no lines arrive).</summary>
    public void Refresh(DateTime now)
    {
        IsIdle = _stream == null || now - _streamAt > StaleAfter;
        if (IsIdle)
        {
            if (_render == null || now - _renderAt > StaleAfter)
            {
                RenderFps = 0;
                DroppedPerSecond = 0;
            }
        }
        OnPropertyChanged(string.Empty);   // refresh every binding on this object
    }

    public void Reset()
    {
        _stream = null;
        _render = null;
        _prevRender = null;
        _resources = null;
        _peakMbps = 0;
        RenderFps = 0;
        DroppedPerSecond = 0;
        IsIdle = true;
        OnPropertyChanged(string.Empty);
    }

    private static string F1(double v) => v.ToString("0.0", CultureInfo.CurrentCulture);
}
