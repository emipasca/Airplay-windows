using UxPlayDesktop.Services;
using UxPlayDesktop.ViewModels;

namespace UxPlayDesktop.Models;

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
/// silent, low-latency extended desktop. 2304x1440 is 16:10 (MacBook ratio) and
/// exactly 1440 px tall, so it maps 1:1 onto a 1440p monitor with no scaling blur.
/// </summary>
public static class WorkPreset
{
    public const int Width = 2304;
    public const int Height = 1440;
    public const int Fps = 60;
    public const string Decoder = "d3d12h264dec"; // engine swaps to d3d12h265dec for HEVC streams
    public const string VideoSink = "d3d12videosink";
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
            PerformancePreset.WorkExtendedDisplay => "~30-60 ms",
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
