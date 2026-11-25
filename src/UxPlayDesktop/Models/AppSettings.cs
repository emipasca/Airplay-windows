using System.Text.Json.Serialization;

namespace UxPlayDesktop.Models;

public class AppSettings
{
    // General
    public string ServerName { get; set; } = "AirPlay Windows";
    public string Language { get; set; } = "auto";
    public bool StartOnBoot { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool AutoStartServer { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;

    // Preset & Streaming
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PerformancePreset Preset { get; set; } = PerformancePreset.WorkExtendedDisplay;
    /// <summary>Work preset jitter buffer: frames are shown on the client's timestamps plus this
    /// fixed delay, which evens out Wi-Fi arrival jitter. 0 = show frames on arrival (lowest
    /// latency, but network jitter shows up as stutter).</summary>
    public int WorkSmoothingBufferMs { get; set; } = WorkPreset.DefaultSmoothingBufferMs;
    /// <summary>Let the engine size the Work buffer from measured frame lateness (overrides
    /// <see cref="WorkSmoothingBufferMs"/>).</summary>
    public bool WorkSmoothingAuto { get; set; } = true;
    /// <summary>Work preset latency mode, switchable live while mirroring:
    /// Fast (minimal buffer), Balanced (small fixed buffer), Smooth (automatic buffer).</summary>
    public WorkLatencyMode WorkLatencyMode { get; set; } = WorkLatencyMode.Smooth;
    /// <summary>Work preset: request 2x ("Retina") resolution so the Mac renders and encodes
    /// twice the detail; the PC scales it down to fit, which looks much sharper.</summary>
    public bool WorkRetina { get; set; } = false;
    /// <summary>Work preset: show the stream 1:1 (window picture area = stream size, centered)
    /// instead of scaling it, which blurs text. Ignored when full-screen is forced.</summary>
    public bool WorkPixelPerfect { get; set; } = true;
    /// <summary>Work preset codec: H.265 (default) or H.264. The Mac's encoders behave
    /// differently at low bitrates; offered as a quality test.</summary>
    public bool WorkUseH265 { get; set; } = true;
    /// <summary>Work preset frame-rate cap sent to the Mac (60 or 30). At 30 the Mac may spend
    /// more bits per frame (sharper while moving) at the cost of choppier motion.</summary>
    public int WorkMaxFps { get; set; } = 60;

    // Video Configuration
    public int DisplayWidth { get; set; } = 1920;
    public int DisplayHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 60;
    public bool EnableH265 { get; set; } = true;
    public bool ForceFullscreen { get; set; } = false;
    public bool BorderlessWindow { get; set; } = true; // Clean borderless display mode
    public bool AlwaysOnTop { get; set; } = false; // Floating picture-in-picture mode
    public int TargetDisplayIndex { get; set; } = 0; // 0 for primary monitor
    public string VideoSink { get; set; } = "d3d11videosink"; // d3d11videosink, d3d12videosink, autovideosink

    // Audio Configuration
    public bool EnableAudio { get; set; } = true;
    public int AudioDelayMs { get; set; } = 0; // Audio/Video sync calibration
    public double AudioVolume { get; set; } = 1.0; // 0.0 to 1.0
    public bool IsMuted { get; set; } = false;
    public string AudioSink { get; set; } = "autoaudiosink"; // autoaudiosink, wasapisink

    // Recording & Captures
    public bool EnableRecording { get; set; } = false;
    public string RecordingDirectory { get; set; } = string.Empty;
    public string ScreenshotsDirectory { get; set; } = string.Empty;

    // Hotkeys & Productivity
    public bool EnableGlobalHotkeys { get; set; } = true;

    // Appearance & Personalization
    public string AccentColor { get; set; } = "#38BDF8"; // Sky Blue (default)
    public bool CompactMode { get; set; } = false;

    // Security & Pairing
    public bool RequirePin { get; set; } = false;
    public string? FixedPin { get; set; } = null; // 4-digit PIN code or null for random
    public string? Password { get; set; } = null;

    // Discovery & Networking
    public bool EnableBluetoothBeacon { get; set; } = true;
    public bool InhibitSleepAndScreensaver { get; set; } = true;
    public int NetworkPort { get; set; } = 0; // 0 for automatic port allocation
    public string NetworkAdapterId { get; set; } = string.Empty; // empty = automatic; otherwise NetworkInterface.Id of the adapter to advertise on

    // Advanced / Diagnostics
    public bool VerboseLogging { get; set; } = false;
    public string AdditionalArguments { get; set; } = string.Empty;
}
