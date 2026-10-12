using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>A working WAV is never re-encoded, and a missing ffmpeg is reported with the fix.</summary>
public class AudioConverterTests : IDisposable
{
    private readonly TempPack _temp = new();

    public void Dispose() => _temp.Dispose();

    private static AudioConverter WithoutFfmpeg()
    {
        return new AudioConverter(new TestLogger<AudioConverter>()) { FfmpegOverride = "" };
    }

    private static AudioConverter Real()
    {
        return new AudioConverter(new TestLogger<AudioConverter>());
    }

    // ------------------------------------------------------------------ no ffmpeg

    [Fact]
    public void A_playable_wav_is_copied_byte_for_byte_rather_than_re_encoded()
    {
        var source = _temp.Wav("in/shot.wav", channels: 2, sampleRate: 44100);
        var destination = _temp.At("out/shot.wav");

        var result = WithoutFfmpeg().Prepare(source, destination);

        Assert.True(result.Ok);
        Assert.False(result.Converted);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
    }

    [Fact]
    public void Stereo_is_left_alone_because_the_plugin_folds_it_on_load()
    {
        var result = WithoutFfmpeg().Prepare(_temp.Wav("in/shot.wav", channels: 2), _temp.At("out/shot.wav"));

        Assert.True(result.Ok);
        Assert.Equal(2, WavInspector.Inspect(_temp.At("out/shot.wav")).Channels);
    }

    [Fact]
    public void Without_ffmpeg_a_non_wav_is_refused_with_both_ways_out()
    {
        var result = WithoutFfmpeg().Prepare(_temp.File_("in/shot.mp3"), _temp.At("out/shot.wav"));

        Assert.False(result.Ok);
        Assert.Contains("only reads WAV", result.Problem);
        Assert.Contains("winget install", result.Fix);
        Assert.Contains("ffmpeg -i", result.Fix);
        Assert.False(File.Exists(_temp.At("out/shot.wav")));
    }

    [Fact]
    public void Without_ffmpeg_an_unreadable_wav_is_refused_with_the_command()
    {
        var result = WithoutFfmpeg().Prepare(_temp.Wav("in/shot.wav", bits: 4), _temp.At("out/shot.wav"));

        Assert.False(result.Ok);
        Assert.StartsWith("ffmpeg -i", result.Fix);
    }

    [Fact]
    public void A_missing_source_is_reported_rather_than_throwing()
    {
        Assert.False(WithoutFfmpeg().Prepare(_temp.At("in/gone.wav"), _temp.At("out/gone.wav")).Ok);
    }

    [Fact]
    public void What_can_be_accepted_is_answerable_without_touching_anything()
    {
        var converter = WithoutFfmpeg();

        Assert.True(converter.CanAccept(_temp.Wav("in/good.wav")));
        Assert.False(converter.CanAccept(_temp.Wav("in/bad.wav", bits: 4)));
        Assert.False(converter.CanAccept(_temp.File_("in/shot.mp3")));
    }

    [Theory]
    [InlineData("shot.wav", true)]
    [InlineData("shot.MP3", true)]
    [InlineData("shot.ogg", true)]
    [InlineData("readme.txt", false)]
    [InlineData("LICENSE", false)]
    public void Only_audio_is_picked_up_from_a_folder(string name, bool audio)
    {
        Assert.Equal(audio, AudioConverter.LooksLikeAudio(name));
    }

    // ------------------------------------------------------------------ with ffmpeg

    /// <summary>Need a real ffmpeg and skip without one.</summary>
    private static bool NoFfmpeg => !Real().HasFfmpeg;

    [Fact]
    public void An_mp3_is_converted_to_mono_48k_pcm16()
    {
        if (NoFfmpeg) Assert.Skip("ffmpeg is not installed.");

        var converter = Real();
        var mp3 = _temp.At("in/tone.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(mp3)!);

        // Made with the same ffmpeg, so this test brings its own fixture.
        Assert.True(Synth(converter, "sine=frequency=800:duration=0.4", mp3));

        var result = converter.Prepare(mp3, _temp.At("out/tone.wav"));

        Assert.True(result.Ok, result.Problem);
        Assert.True(result.Converted);

        var info = WavInspector.Inspect(_temp.At("out/tone.wav"));
        Assert.True(info.Ok);
        Assert.Equal(1, info.Channels);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(16, info.Bits);
        Assert.Equal(0.4f, info.Duration, 0.05f);
    }

    [Fact]
    public void A_stereo_mp3_comes_out_mono_so_unity_can_place_it_in_3d()
    {
        if (NoFfmpeg) Assert.Skip("ffmpeg is not installed.");

        var converter = Real();
        var mp3 = _temp.At("in/stereo.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(mp3)!);

        Assert.True(Synth(converter, "sine=frequency=500:duration=0.3", mp3, stereo: true));

        Assert.True(converter.Prepare(mp3, _temp.At("out/stereo.wav")).Ok);
        Assert.Equal(1, WavInspector.Inspect(_temp.At("out/stereo.wav")).Channels);
    }

    [Fact]
    public void A_file_that_is_not_really_audio_is_refused_even_with_ffmpeg_present()
    {
        if (NoFfmpeg) Assert.Skip("ffmpeg is not installed.");

        var result = Real().Prepare(_temp.File_("in/lying.mp3", "this is plain text"), _temp.At("out/lying.wav"));

        Assert.False(result.Ok);
        Assert.False(File.Exists(_temp.At("out/lying.wav")));
    }

    [Fact]
    public void An_adpcm_wav_is_rescued_by_re_encoding_it()
    {
        if (NoFfmpeg) Assert.Skip("ffmpeg is not installed.");

        var converter = Real();
        var adpcm = _temp.At("in/adpcm.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(adpcm)!);

        Assert.True(Synth(converter, "sine=frequency=600:duration=0.3", adpcm, codec: "adpcm_ima_wav"));
        Assert.False(WavInspector.Inspect(adpcm).Ok);

        var result = converter.Prepare(adpcm, _temp.At("out/adpcm.wav"));

        Assert.True(result.Ok, result.Problem);
        Assert.True(WavInspector.Inspect(_temp.At("out/adpcm.wav")).Ok);
    }

    /// <summary>Builds a fixture with ffmpeg itself, so these tests need nothing checked in.</summary>
    private static bool Synth(AudioConverter converter, string lavfi, string destination,
                              bool stereo = false, string? codec = null)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", lavfi,
            "-ac", stereo ? "2" : "1"
        };

        if (codec is not null)
            args.AddRange(["-c:a", codec]);

        args.Add(destination);

        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = converter.FfmpegPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        foreach (var a in args)
            start.ArgumentList.Add(a);

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        return process.ExitCode == 0 && File.Exists(destination);
    }
}
