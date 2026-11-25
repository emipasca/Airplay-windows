using UxPlayDesktop.Services;
using UxPlayDesktop.ViewModels;

namespace UxPlayDesktop.Models;

/// <summary>Work preset latency modes (see <see cref="WorkPreset"/> buffers).</summary>
public enum WorkLatencyMode
{
    Fast,
    Balanced,
    Smooth
}

public enum PerformancePreset
{
    TeachingAndPresentation,
    LowLatencyGaming,
    HighQualityCinema,
    AudioOnly,
    Custom,
    WorkExtendedDisplay
}

/// <summary>
/// Fixed stream profile for the "Work" preset: a MacBook used with this PC as a
/// silent, low-latency extended desktop. 1728x1117 is the MacBook Pro 16" default
/// "looks like" size, so windows keep the same size when moved to this display.
/// </summary>
public static class WorkPreset
{
    public const int Width = 1728;
    public const int Height = 1117;
    /// <summary>2x ("Retina") request: the Mac renders HiDPI and sends twice the detail.</summary>
    public const int RetinaWidth = Width * 2;
    public const int RetinaHeight = Height * 2;
    /// <summary>MacBook Pro 16" panel size in mm (3456x2234 at ~254 ppi), reported so macOS
    /// recognises a high-density display and lays it out at 1728x1117 points.</summary>
    public const int RetinaPhysicalWidthMm = 345;
    public const int RetinaPhysicalHeightMm = 223;

    /// <summary>Fixed buffers for the live latency modes (Smooth is automatic).</summary>
    public const int FastBufferMs = 12;
    public const int BalancedBufferMs = 35;
    public const int Fps = 60;
    public const string Decoder = "d3d12h264dec"; // engine swaps to d3d12h265dec for HEVC streams
    public const string VideoSink = "d3d12videosink";

    /// <summary>Measured on a MacBook over Wi-Fi: most frames land within ~10 ms of the fastest,
    /// with a burst ~70 ms late about once a second. 60 ms (+ decode allowance + the sink's
    /// 20 ms lateness grace) rides through nearly all of those bursts.</summary>
    public const int DefaultSmoothingBufferMs = 60;
    public const int MaxSmoothingBufferMs = 150;

    /// <summary>Time allowed for GPU decode + present. Pinned instead of the pipeline's queried
    /// worst case (~55 ms: decoder reorder estimate + the sink's 20 ms processing deadline).</summary>
    public const int DecodeAllowanceMs = 10;
}

public class PresetOption : ViewModelBase
{
    public PerformancePreset Preset { get; }

    public PresetOption(PerformancePreset preset)
    {
        Preset = preset;
    }

    public string DisplayName => Preset.GetDisplayName();
    public string Description => Preset.GetDescription();
    public string ExpectedDelay => Preset.GetExpectedDelay();

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ExpectedDelay));
    }

    public override string ToString() => DisplayName;
}

public static class PresetExtensions
{
    public static string GetExpectedDelay(this PerformancePreset preset)
    {
        return preset switch
        {
            PerformancePreset.LowLatencyGaming => "~30-50 ms",
            PerformancePreset.TeachingAndPresentation => "~120-150 ms",
            PerformancePreset.HighQualityCinema => "~500-1000 ms",
            PerformancePreset.AudioOnly => "~200-250 ms",
            PerformancePreset.Custom => "Configurable",
            PerformancePreset.WorkExtendedDisplay => "Fast ~25 ms / Smooth auto",
            _ => string.Empty
        };
    }

    public static string GetDisplayName(this PerformancePreset preset)
    {
        var loc = LocalizationService.Instance;
        return preset switch
        {
            PerformancePreset.TeachingAndPresentation => loc["PresetTeaching"],
            PerformancePreset.LowLatencyGaming => loc["PresetGaming"],
            PerformancePreset.HighQualityCinema => loc["PresetCinema"],
            PerformancePreset.AudioOnly => loc["PresetAudio"],
            PerformancePreset.Custom => loc["PresetCustom"],
            PerformancePreset.WorkExtendedDisplay => loc["PresetWork"],
            _ => preset.ToString()
        };
    }

    public static string GetDescription(this PerformancePreset preset)
    {
        var loc = LocalizationService.Instance;
        return preset switch
        {
            PerformancePreset.TeachingAndPresentation => loc["PresetTeachingDesc"],
            PerformancePreset.LowLatencyGaming => loc["PresetGamingDesc"],
            PerformancePreset.HighQualityCinema => loc["PresetCinemaDesc"],
            PerformancePreset.AudioOnly => loc["PresetAudioDesc"],
            PerformancePreset.Custom => loc["PresetCustomDesc"],
            PerformancePreset.WorkExtendedDisplay => loc["PresetWorkDesc"],
            _ => string.Empty
        };
    }
}
