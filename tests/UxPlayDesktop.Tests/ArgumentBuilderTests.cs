using System.Linq;
using UxPlayDesktop.Models;
using UxPlayDesktop.Services;
using Xunit;

namespace UxPlayDesktop.Tests;

public class ArgumentBuilderTests
{
    private readonly ArgumentBuilder _builder = new();

    [Fact]
    public void BuildArguments_TeachingPreset_ContainsExpectedArguments()
    {
        var settings = new AppSettings
        {
            ServerName = "Math Lecture Mirror",
            Preset = PerformancePreset.TeachingAndPresentation,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            TargetFps = 60,
            EnableH265 = true,
            InhibitSleepAndScreensaver = true
        };

        var args = _builder.BuildArguments(settings).ToList();

        Assert.Contains("-n", args);
        Assert.Contains("Math Lecture Mirror", args);
        Assert.DoesNotContain("-nh", args);   // engine appends "@<hostname>" to the name
        Assert.Contains("-s", args);
        Assert.Contains("1920x1080", args);
        Assert.Contains("-fps", args);
        Assert.Contains("60", args);
        Assert.Contains("-vs", args);
        Assert.Contains("d3d11videosink", args);
        Assert.Contains("-h265", args);
    }

    [Fact]
    public void BuildArguments_WorkPreset_Uses1440p16x10GpuPipelineWithoutAudio()
    {
        var settings = new AppSettings
        {
            Preset = PerformancePreset.WorkExtendedDisplay,
            EnableAudio = true,        // Work forces audio off regardless
            DisplayWidth = 3440,       // and ignores the generic resolution fields
            DisplayHeight = 1440,
            AudioDelayMs = 120
        };

        var args = _builder.BuildArguments(settings).ToList();

        Assert.Equal("2304x1440@60", args[args.IndexOf("-s") + 1]);
        Assert.Equal("60", args[args.IndexOf("-fps") + 1]);
        Assert.Contains("-h265", args);
        Assert.Equal("d3d12h264dec", args[args.IndexOf("-vd") + 1]);
        Assert.Equal("d3d12videosink", args[args.IndexOf("-vs") + 1]);
        Assert.Equal("no", args[args.IndexOf("-vsync") + 1]);
        Assert.Single(args, a => a == "-vsync");
        Assert.Equal("0", args[args.IndexOf("-as") + 1]);
        Assert.DoesNotContain("3440x1440", args);
    }

    [Fact]
    public void Work_IsDefaultPreset_And16x10()
    {
        Assert.Equal(PerformancePreset.WorkExtendedDisplay, new AppSettings().Preset);
        Assert.Equal(16.0 / 10.0, (double)WorkPreset.Width / WorkPreset.Height, 3);
    }

    [Fact]
    public void BuildArguments_AudioOnlyPreset_DisablesVideoSink()
    {
        var settings = new AppSettings
        {
            Preset = PerformancePreset.AudioOnly
        };

        var args = _builder.BuildArguments(settings).ToList();

        var vsIndex = args.IndexOf("-vs");
        Assert.True(vsIndex >= 0);
        Assert.Equal("0", args[vsIndex + 1]);
        Assert.DoesNotContain("-fs", args);
    }

    [Fact]
    public void BuildArguments_LowLatencyGamingPreset_AddsVsyncNo()
    {
        var settings = new AppSettings
        {
            Preset = PerformancePreset.LowLatencyGaming
        };

        var args = _builder.BuildArguments(settings).ToList();

        var vsyncIndex = args.IndexOf("-vsync");
        Assert.True(vsyncIndex >= 0);
        Assert.Equal("no", args[vsyncIndex + 1]);
    }

    [Fact]
    public void BuildArguments_FixedPinAuthentication_PassesPinValue()
    {
        var settings = new AppSettings
        {
            RequirePin = true,
            FixedPin = "4821"
        };

        var args = _builder.BuildArguments(settings).ToList();

        var pinIndex = args.IndexOf("-pin");
        Assert.True(pinIndex >= 0);
        Assert.Equal("4821", args[pinIndex + 1]);
    }

    [Fact]
    public void BuildArguments_ZeroPin_DoesNotPassFixedPin_BecauseZeroIsInvalidInUxPlay()
    {
        var settings = new AppSettings
        {
            RequirePin = true,
            FixedPin = "0000"
        };

        var args = _builder.BuildArguments(settings).ToList();

        var pinIndex = args.IndexOf("-pin");
        Assert.True(pinIndex >= 0);
        // Next argument should not be "0000"
        if (pinIndex + 1 < args.Count)
        {
            Assert.NotEqual("0000", args[pinIndex + 1]);
        }
    }

    [Fact]
    public void BuildArguments_AudioDelayCalibration_PassesVsyncDelayMs()
    {
        var settings = new AppSettings
        {
            Preset = PerformancePreset.TeachingAndPresentation,
            EnableAudio = true,
            AudioDelayMs = 75
        };

        var args = _builder.BuildArguments(settings).ToList();

        var delayIndex = args.IndexOf("-vsync");
        Assert.True(delayIndex >= 0);
        Assert.Equal("75", args[delayIndex + 1]);
        Assert.DoesNotContain("-delay", args);
    }

    [Fact]
    public void BuildCommandLineString_ProperlyQuotesArgumentsWithSpaces()
    {
        var settings = new AppSettings
        {
            ServerName = "Jorge's Living Room PC"
        };

        var cmdLine = _builder.BuildCommandLineString(settings);

        Assert.Contains("-n \"Jorge's Living Room PC\"", cmdLine);
    }

    [Fact]
    public void BuildArguments_EmptyServerName_FallsBackToMachineName()
    {
        var settings = new AppSettings
        {
            ServerName = "   "
        };

        var args = _builder.BuildArguments(settings).ToList();
        var nIndex = args.IndexOf("-n");
        Assert.True(nIndex >= 0);
        Assert.Equal(System.Environment.MachineName, args[nIndex + 1]);
    }

    [Fact]
    public void BuildArguments_NegativeAudioDelay_AppliesNegativeVsync()
    {
        var settings = new AppSettings
        {
            Preset = PerformancePreset.TeachingAndPresentation,
            AudioDelayMs = -120
        };

        var args = _builder.BuildArguments(settings).ToList();
        var delayIndex = args.IndexOf("-vsync");
        Assert.True(delayIndex >= 0);
        Assert.Equal("-120", args[delayIndex + 1]);
    }

    [Fact]
    public void SplitCommandLine_HandlesTabsAndEscapedQuotes()
    {
        var cmd = "-vs\td3d11videosink -name \"My \\\"Special\\\" Screen\" -fps 60";
        var tokens = ArgumentBuilder.SplitCommandLine(cmd);

        Assert.Equal(6, tokens.Count);
        Assert.Equal("-vs", tokens[0]);
        Assert.Equal("d3d11videosink", tokens[1]);
        Assert.Equal("-name", tokens[2]);
        Assert.Equal("My \"Special\" Screen", tokens[3]);
        Assert.Equal("-fps", tokens[4]);
        Assert.Equal("60", tokens[5]);
    }

    [Fact]
    public void BuildArguments_RecordingEnabled_AddsMp4FlagAndPath()
    {
        var settings = new AppSettings
        {
            EnableRecording = true,
            RecordingDirectory = @"C:\TestRecordings"
        };

        var args = _builder.BuildArguments(settings).ToList();
        var mp4Index = args.IndexOf("-mp4");

        Assert.True(mp4Index >= 0);
        Assert.Equal(@"C:\TestRecordings\AirPlay_Recording", args[mp4Index + 1]);
    }

    [Fact]
    public void BuildArguments_Muted_AddsAudioSinkZero()
    {
        var settings = new AppSettings
        {
            IsMuted = true
        };

        var args = _builder.BuildArguments(settings).ToList();
        var asIndex = args.IndexOf("-as");

        Assert.True(asIndex >= 0);
        Assert.Equal("0", args[asIndex + 1]);
    }

    [Fact]
    public void BuildArguments_VolumeControl_AddsVolFlag()
    {
        var settings = new AppSettings
        {
            Preset = PerformancePreset.TeachingAndPresentation,
            IsMuted = false,
            AudioVolume = 0.65
        };

        var args = _builder.BuildArguments(settings).ToList();
        var volIndex = args.IndexOf("-vol");

        Assert.True(volIndex >= 0);
        Assert.Equal("0.65", args[volIndex + 1]);
    }
}
