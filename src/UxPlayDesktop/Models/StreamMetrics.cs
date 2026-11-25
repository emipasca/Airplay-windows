using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace UxPlayDesktop.Models;

/// <summary>Once-per-second network-side sample from the engine ("METRICS stream ...").</summary>
public sealed record StreamSample(
    double Fps,
    double Kbps,
    int Keyframes,
    double? NetLatencyMs,
    double? NetLatencyMaxMs,
    string Codec);

/// <summary>Once-per-second render-side sample from the engine ("METRICS render ...").
/// Rendered and Dropped are cumulative frame counters of the video sink.</summary>
public sealed record RenderSample(
    ulong Rendered,
    ulong Dropped,
    double? PipelineLatencyMs);

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
        sample = new StreamSample(
            fps,
            kbps,
            (int)keyframes,
            NonNegative(f, "net_latency_ms"),
            NonNegative(f, "net_latency_max_ms"),
            f.TryGetValue("codec", out var codec) ? codec : "h264");
        return true;
    }

    public static bool TryParseRender(string line, out RenderSample sample)
    {
        sample = null!;
        if (!TryGetFields(line, "render", out var f)) return false;
        if (!f.TryGetValue("rendered", out var r) || !ulong.TryParse(r, out var rendered)) return false;
        if (!f.TryGetValue("dropped", out var d) || !ulong.TryParse(d, out var dropped)) return false;

        sample = new RenderSample(rendered, dropped, NonNegative(f, "pipeline_latency_ms"));
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

    /// <summary>The engine writes -1 when a value is unavailable.</summary>
    private static double? NonNegative(Dictionary<string, string> f, string key) =>
        TryDouble(f, key, out var v) && v >= 0 ? v : null;
}
