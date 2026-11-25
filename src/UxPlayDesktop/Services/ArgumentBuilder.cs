using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UxPlayDesktop.Models;

namespace UxPlayDesktop.Services;

public interface IArgumentBuilder
{
    IReadOnlyList<string> BuildArguments(AppSettings settings, string? bleStatusPath = null);
    string BuildCommandLineString(AppSettings settings, string? bleStatusPath = null);
}

public class ArgumentBuilder : IArgumentBuilder
{
    public IReadOnlyList<string> BuildArguments(AppSettings settings, string? bleStatusPath = null)
    {
        var args = new List<string>();

        // Server display name (airplay device name)
        var serverName = string.IsNullOrWhiteSpace(settings.ServerName)
            ? Environment.MachineName
            : settings.ServerName.Trim();
        args.Add("-n");
        args.Add(serverName);

        // No "-nh": let the engine append "@<PC hostname>" so the receiver is
        // discoverable as e.g. "AirPlay Windows@Emilio-PC25".

        // Apply Preset specific configurations
        switch (settings.Preset)
        {
            case PerformancePreset.TeachingAndPresentation:
                // Optimized for crisp static text, 1080p, 60fps, D3D11 sink, screensaver inhibitor
                args.Add("-s");
                args.Add($"{settings.DisplayWidth}x{settings.DisplayHeight}");
                args.Add("-fps");
                args.Add($"{settings.TargetFps}");
                args.Add("-vs");
                args.Add(string.IsNullOrWhiteSpace(settings.VideoSink) ? "d3d11videosink" : settings.VideoSink);
                if (settings.EnableH265) args.Add("-h265");
                break;

            case PerformancePreset.LowLatencyGaming:
                // Low latency, 60fps, sync=false
                args.Add("-s");
                args.Add($"{settings.DisplayWidth}x{settings.DisplayHeight}");
                args.Add("-fps");
                args.Add("60");
                args.Add("-vsync");
                args.Add("no");
                args.Add("-vs");
                args.Add(string.IsNullOrWhiteSpace(settings.VideoSink) ? "d3d11videosink" : settings.VideoSink);
                break;

            case PerformancePreset.HighQualityCinema:
                // 4K capable, H.265 decode enabled
                args.Add("-s");
                args.Add($"{Math.Max(settings.DisplayWidth, 3840)}x{Math.Max(settings.DisplayHeight, 2160)}");
                args.Add("-fps");
                args.Add("60");
                args.Add("-h265");
                args.Add("-vs");
                args.Add(string.IsNullOrWhiteSpace(settings.VideoSink) ? "d3d11videosink" : settings.VideoSink);
                break;

            case PerformancePreset.AudioOnly:
                // Audio only: disable video sink (-vs 0)
                args.Add("-vs");
                args.Add("0");
                break;

            case PerformancePreset.WorkExtendedDisplay:
            {
                // MacBook as extended desktop at the MacBook Pro 14" size (optionally 2x "Retina"
                // so the Mac encodes twice the detail), 60 fps, H.265 + D3D12 GPU decode/render
                // with no cross-API copies. Audio is forced off below (sound stays on the Mac).
                var (w, h) = settings.WorkRetina
                    ? (WorkPreset.RetinaWidth, WorkPreset.RetinaHeight)
                    : (WorkPreset.Width, WorkPreset.Height);
                args.Add("-s");
                args.Add($"{w}x{h}@{WorkPreset.Fps}");
                if (settings.WorkRetina)
                {
                    // Describe a Retina panel (Airplay-windows engine options): 1512x982 points,
                    // 3024x1964 pixels, MacBook Pro 14" physical size.
                    args.Add("-hidpi");
                    args.Add("-physical");
                    args.Add($"{WorkPreset.RetinaPhysicalWidthMm}x{WorkPreset.RetinaPhysicalHeightMm}");
                }
                // Quality test knobs: frame-rate cap (30 or 60) and codec (H.265 or H.264).
                var maxFps = settings.WorkMaxFps <= 30 ? 30 : WorkPreset.Fps;
                args.Add("-fps");
                args.Add($"{maxFps}");
                if (settings.WorkUseH265)
                {
                    args.Add("-h265");
                }
                args.Add("-vd");
                args.Add(WorkPreset.Decoder);   // engine swaps to the H.265 decoder for HEVC streams

                var bufferMs = Math.Clamp(settings.WorkSmoothingBufferMs, 0, WorkPreset.MaxSmoothingBufferMs);
                if (settings.WorkSmoothingAuto)
                {
                    // Engine measures frame lateness and sizes the buffer itself (Airplay-windows option).
                    args.Add("-vs");
                    args.Add(WorkPreset.VideoSink);
                    args.Add("-vlatency");
                    args.Add("auto");
                    args.Add("-vadapt");
                    AddLiveLatencyControl(args, settings);
                }
                else if (bufferMs > 0)
                {
                    // Jitter buffer: keep timestamp sync on (frames paced by the client's clock) and
                    // pin the pipeline latency to decode time + buffer, so each frame is shown a fixed
                    // (decode + buffer) after its schedule. "-vlatency" is an Airplay-windows engine option.
                    args.Add("-vs");
                    args.Add(WorkPreset.VideoSink);
                    args.Add("-vlatency");
                    args.Add($"{WorkPreset.DecodeAllowanceMs + bufferMs}");
                    // Pace on the fastest recent frame, not on the (often very late) first frame.
                    args.Add("-vadapt");
                    AddLiveLatencyControl(args, settings);
                }
                else
                {
                    // Show frames the moment they arrive: lowest latency, jitter passes through.
                    args.Add("-vs");
                    args.Add(WorkPreset.VideoSink);
                    args.Add("-vsync");
                    args.Add("no");
                }
                break;
            }

            case PerformancePreset.Custom:
                args.Add("-s");
                args.Add($"{settings.DisplayWidth}x{settings.DisplayHeight}");
                args.Add("-fps");
                args.Add($"{settings.TargetFps}");
                if (!string.IsNullOrWhiteSpace(settings.VideoSink))
                {
                    args.Add("-vs");
                    args.Add(settings.VideoSink);
                }
                if (settings.EnableH265) args.Add("-h265");
                break;
        }

        // Fullscreen
        if (settings.ForceFullscreen && settings.Preset != PerformancePreset.AudioOnly)
        {
            args.Add("-fs");
        }

        // Audio
        if (!settings.EnableAudio || settings.IsMuted || settings.Preset == PerformancePreset.WorkExtendedDisplay)
        {
            args.Add("-as");
            args.Add("0");
        }
        else
        {
            if (settings.Preset != PerformancePreset.LowLatencyGaming && settings.AudioDelayMs != 0)
            {
                var clampedDelay = Math.Clamp(settings.AudioDelayMs, -999, 999);
                args.Add("-vsync");
                args.Add(clampedDelay.ToString());
            }
            if (!string.IsNullOrWhiteSpace(settings.AudioSink) && settings.AudioSink != "autoaudiosink")
            {
                args.Add("-as");
                args.Add(settings.AudioSink);
            }
            if (settings.AudioVolume >= 0.0 && settings.AudioVolume < 0.99)
            {
                var vol = Math.Clamp(settings.AudioVolume, 0.0, 1.0);
                args.Add("-vol");
                args.Add(vol.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // MP4 Recording
        if (settings.EnableRecording)
        {
            var dir = string.IsNullOrWhiteSpace(settings.RecordingDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "AirPlay Desktop")
                : settings.RecordingDirectory;

            try { Directory.CreateDirectory(dir); } catch { }
            var baseFileName = Path.Combine(dir, "AirPlay_Recording");
            args.Add("-mp4");
            args.Add(baseFileName);
        }

        // Security / Pairing
        if (settings.RequirePin)
        {
            args.Add("-pin");
            if (!string.IsNullOrWhiteSpace(settings.FixedPin) &&
                settings.FixedPin.Length == 4 &&
                int.TryParse(settings.FixedPin, out var pinVal) &&
                pinVal >= 1 && pinVal <= 9999)
            {
                args.Add(settings.FixedPin);
            }
        }
        else if (!string.IsNullOrWhiteSpace(settings.Password))
        {
            args.Add("-pw");
            args.Add(settings.Password);
        }

        // Network port
        if (settings.NetworkPort > 0)
        {
            args.Add("-p");
            args.Add(settings.NetworkPort.ToString());
        }

        // Network adapter to advertise on (-i <ipv4>); omitted = automatic selection
        var adapterIpv4 = NetworkAdapterService.ResolveIpv4(settings.NetworkAdapterId);
        if (!string.IsNullOrEmpty(adapterIpv4))
        {
            args.Add("-i");
            args.Add(adapterIpv4);
        }

        // Bluetooth Low Energy Beacon
        if (settings.EnableBluetoothBeacon && !string.IsNullOrWhiteSpace(bleStatusPath))
        {
            args.Add("-ble");
            args.Add(bleStatusPath);
        }

        // Additional user arguments
        if (!string.IsNullOrWhiteSpace(settings.AdditionalArguments))
        {
            var parsed = SplitCommandLine(settings.AdditionalArguments);
            args.AddRange(parsed);
        }

        return args.AsReadOnly();
    }

    /// <summary>Work preset: let the app switch latency modes live over the engine's stdin
    /// (-vcontrol), and start with the saved mode's fixed buffer (-vbuffer) for Fast/Balanced.</summary>
    private static void AddLiveLatencyControl(List<string> args, AppSettings settings)
    {
        args.Add("-vcontrol");
        var buffer = BufferForMode(settings.WorkLatencyMode);
        if (buffer is int ms)
        {
            args.Add("-vbuffer");
            args.Add(ms.ToString());
        }
    }

    /// <summary>Fixed buffer for a latency mode, or null for Smooth (automatic).</summary>
    public static int? BufferForMode(WorkLatencyMode mode) => mode switch
    {
        WorkLatencyMode.Fast => WorkPreset.FastBufferMs,
        WorkLatencyMode.Balanced => WorkPreset.BalancedBufferMs,
        _ => null
    };

    /// <summary>Engine control line that switches a running engine to a latency mode.</summary>
    public static string CommandForMode(WorkLatencyMode mode) =>
        BufferForMode(mode) is int ms ? $"buffer {ms}" : "mode smooth";

    public string BuildCommandLineString(AppSettings settings, string? bleStatusPath = null)
    {
        var args = BuildArguments(settings, bleStatusPath);
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"'))
            {
                sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }
        return sb.ToString();
    }

    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return list;

        var sb = new StringBuilder();
        bool inQuotes = false;
        bool escapeNext = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (escapeNext)
            {
                sb.Append(c);
                escapeNext = false;
            }
            else if (c == '\\')
            {
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    escapeNext = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list;
    }
}
