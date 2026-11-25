using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace UxPlayDesktop.Services;

public sealed record EngineResourceSample(
    double CpuPercent,
    double RamMb,
    double? GpuDecodePercent,
    double? Gpu3dPercent);

/// <summary>
/// Samples the AirPlay engine process: CPU (share of the whole machine), working set,
/// and its use of the GPU video-decode and 3D engines from the Windows "GPU Engine"
/// performance counters. Call <see cref="Sample"/> about once per second from a
/// background thread; the first call primes the counters and reports 0.
/// </summary>
public sealed class EngineResourceSampler : IDisposable
{
    private int _pid = -1;
    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt;
    private List<PerformanceCounter> _decodeCounters = new();
    private List<PerformanceCounter> _3dCounters = new();
    private DateTime _countersRefreshedAt = DateTime.MinValue;
    private static readonly TimeSpan CounterRefreshInterval = TimeSpan.FromSeconds(5);

    public EngineResourceSample? Sample(int? pid)
    {
        if (pid is null) { Reset(); return null; }

        Process process;
        try { process = Process.GetProcessById(pid.Value); }
        catch { Reset(); return null; }

        using (process)
        {
            if (pid.Value != _pid)
            {
                Reset();
                _pid = pid.Value;
            }

            double cpu = 0;
            var now = DateTime.UtcNow;
            TimeSpan totalCpu;
            try { totalCpu = process.TotalProcessorTime; }
            catch { return null; }

            if (_lastCpuAt != default)
            {
                var wall = (now - _lastCpuAt).TotalMilliseconds;
                if (wall > 0)
                {
                    cpu = (totalCpu - _lastCpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100.0;
                }
            }
            _lastCpu = totalCpu;
            _lastCpuAt = now;

            double ramMb = 0;
            try { ramMb = process.WorkingSet64 / (1024.0 * 1024.0); } catch { }

            RefreshGpuCounters(now);
            return new EngineResourceSample(
                Math.Clamp(cpu, 0, 100),
                ramMb,
                SumCounters(_decodeCounters),
                SumCounters(_3dCounters));
        }
    }

    private void RefreshGpuCounters(DateTime now)
    {
        if (now - _countersRefreshedAt < CounterRefreshInterval) return;
        _countersRefreshedAt = now;

        try
        {
            var prefix = $"pid_{_pid}_";
            var instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            var decode = instances.Where(n => n.EndsWith("engtype_VideoDecode", StringComparison.Ordinal)).ToHashSet();
            var threeD = instances.Where(n => n.EndsWith("engtype_3D", StringComparison.Ordinal)).ToHashSet();

            // Keep counters that still exist (so their rate baselines survive), add new ones.
            _decodeCounters = Sync(_decodeCounters, decode);
            _3dCounters = Sync(_3dCounters, threeD);
        }
        catch
        {
            // GPU counters unavailable (old driver / permissions): report null.
        }
    }

    private static List<PerformanceCounter> Sync(List<PerformanceCounter> current, HashSet<string> wanted)
    {
        var kept = new List<PerformanceCounter>();
        foreach (var c in current)
        {
            if (wanted.Remove(c.InstanceName)) kept.Add(c);
            else c.Dispose();
        }
        foreach (var name in wanted)
        {
            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true);
            try { counter.NextValue(); } catch { }   // prime: first read is always 0
            kept.Add(counter);
        }
        return kept;
    }

    private static double? SumCounters(List<PerformanceCounter> counters)
    {
        if (counters.Count == 0) return null;
        double total = 0;
        foreach (var c in counters)
        {
            try { total += c.NextValue(); } catch { }
        }
        return Math.Clamp(total, 0, 100);
    }

    private void Reset()
    {
        _pid = -1;
        _lastCpu = TimeSpan.Zero;
        _lastCpuAt = default;
        _countersRefreshedAt = DateTime.MinValue;
        foreach (var c in _decodeCounters) c.Dispose();
        foreach (var c in _3dCounters) c.Dispose();
        _decodeCounters = new();
        _3dCounters = new();
    }

    public void Dispose() => Reset();
}
