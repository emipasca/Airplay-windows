using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UxPlayDesktop.Models;

/// <summary>How the engine measured per-frame delivery delay.</summary>
public enum LatencyReference
{
    /// <summary>No measurement this second.</summary>
    None,
    /// <summary>Absolute: client frame time (NTP-synced clocks) to arrival — encode + network.</summary>
    Absolute,
    /// <summary>Relative to the first video frame (client never synced its clock, e.g. video-only
    /// sessions). Shows how late frames run versus the pacing schedule; can be negative.</summary>
    Relative
}

/// <summary>Once-per-second network-side sample from the engine ("METRICS stream ...").</summary>
public sealed record StreamSample(
    double Fps,
    double Kbps,
    int Keyframes,
    double? NetLatencyMs,
    double? NetLatencyMaxMs,
    string Codec,
    double? NetLatencyMinMs = null,
    LatencyReference LatencyRef = LatencyReference.Absolute)
{
    /// <summary>Spread between the slowest and fastest frame delivery in the last second.
    /// High jitter with sync off is what makes motion stutter.</summary>
    public double? JitterMs => NetLatencyMaxMs is double max && NetLatencyMinMs is double min ? Math.Max(0, max - min) : null;
}

/// <summary>Once-per-second render-side sample from the engine ("METRICS render ...").
/// Rendered and Dropped are cumulative frame counters of the video sink.</summary>
public sealed record RenderSample(
    ulong Rendered,
    ulong Dropped,
    double? PipelineLatencyMs,
    double? MetricsCostMs = null);

/// <summary>Parses the METRICS lines added to the engine in core/lib/raop_rtp_mirror.c
/// and core/renderers/video_renderer.c.</summary>
public static class StreamMetricsParser
{
    public const string Prefix = "METRICS ";

    private static readonly Regex KeyValue = new(@"(\w+)=([^\s]+)", RegexOptions.Compiled);

    public static bool IsMetricsLine(string? line) =>
        line != null && line.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryParseStream(string line, out StreamSample sample)
    {
        sample = null!;
        if (!TryGetFields(line, "stream", out var f)) return false;
        if (!TryDouble(f, "fps", out var fps) || !TryDouble(f, "kbps", out var kbps)) return false;

        TryDouble(f, "keyframes", out var keyframes);

        // Older engines had no latency_ref and used -1 for "unavailable" (values were absolute).
        var reference = f.TryGetValue("latency_ref", out var r)
            ? r switch { "abs" => LatencyReference.Absolute, "rel" => LatencyReference.Relative, _ => LatencyReference.None }
            : LatencyReference.Absolute;
        bool legacy = !f.ContainsKey("latency_ref");

        double? Latency(string key)
        {
            if (reference == LatencyReference.None || !TryDouble(f, key, out var v)) return null;
            if (legacy && v < 0) return null;
            return v;
        }

        sample = new StreamSample(
            fps,
            kbps,
            (int)keyframes,
            Latency("net_latency_ms"),
            Latency("net_latency_max_ms"),
            f.TryGetValue("codec", out var codec) ? codec : "h264",
            Latency("net_latency_min_ms"),
            reference);
        return true;
    }

    public static bool TryParseRender(string line, out RenderSample sample)
    {
        sample = null!;
        if (!TryGetFields(line, "render", out var f)) return false;
        if (!f.TryGetValue("rendered", out var r) || !ulong.TryParse(r, out var rendered)) return false;
        if (!f.TryGetValue("dropped", out var d) || !ulong.TryParse(d, out var dropped)) return false;

        sample = new RenderSample(rendered, dropped,
            TryDouble(f, "pipeline_latency_ms", out var p) && p >= 0 ? p : null,
            TryDouble(f, "metrics_cost_ms", out var c) && c >= 0 ? c : null);
        return true;
    }

    private static bool TryGetFields(string line, string kind, out Dictionary<string, string> fields)
    {
        fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (line == null) return false;
        var trimmed = line.Trim();
        var head = Prefix + kind + " ";
        if (!trimmed.StartsWith(head, StringComparison.Ordinal)) return false;

        foreach (Match m in KeyValue.Matches(trimmed.Substring(head.Length)))
        {
            fields[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return true;
    }

    private static bool TryDouble(Dictionary<string, string> f, string key, out double value)
    {
        value = 0;
        return f.TryGetValue(key, out var s) &&
               double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
