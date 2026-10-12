using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>The format gate. Each case mirrors a check in the plugin's WavLoader.</summary>
public class WavInspectorTests
{
    [Fact]
    public void Accepts_mono_16_bit_pcm()
    {
        using var t = new TempPack();

        var info = WavInspector.Inspect(t.Wav("shot.wav"));

        Assert.True(info.Ok);
        Assert.Null(info.Problem);
        Assert.Null(info.Note);
        Assert.Equal(1, info.Channels);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(0.4f, info.Duration, 0.01f);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Accepts_every_pcm_bit_depth_the_plugin_reads(int bits)
    {
        using var t = new TempPack();

        Assert.True(WavInspector.Inspect(t.Wav("shot.wav", bits: bits)).Ok);
    }

    [Fact]
    public void Accepts_float32()
    {
        using var t = new TempPack();

        Assert.True(WavInspector.Inspect(t.Wav("shot.wav", bits: 32, format: 3)).Ok);
    }

    [Fact]
    public void Notes_stereo_rather_than_rejecting_it()
    {
        using var t = new TempPack();

        var info = WavInspector.Inspect(t.Wav("shot.wav", channels: 2));

        Assert.True(info.Ok);
        Assert.Contains("folded to mono", info.Note);
    }

    [Fact]
    public void Rejects_an_unsupported_bit_depth()
    {
        using var t = new TempPack();

        var info = WavInspector.Inspect(t.Wav("shot.wav", bits: 4));

        Assert.False(info.Ok);
        Assert.Contains("PCM 16-bit", info.Problem);
    }

    [Fact]
    public void Rejects_a_compressed_wav()
    {
        using var t = new TempPack();

        // 0x11 is IMA ADPCM, which is what "save as wav" in the wrong tool produces.
        var info = WavInspector.Inspect(t.Wav("shot.wav", format: 0x11));

        Assert.False(info.Ok);
        Assert.Contains("compressed WAV", info.Problem);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_a_wav_at_all()
    {
        using var t = new TempPack();

        var info = WavInspector.Inspect(t.File_("shot.wav"));

        Assert.False(info.Ok);
        Assert.Contains("RIFF", info.Problem);
    }

    [Fact]
    public void Rejects_a_wav_with_no_audio_in_it()
    {
        using var t = new TempPack();

        var info = WavInspector.Inspect(t.Wav("shot.wav", seconds: 0));

        Assert.False(info.Ok);
        Assert.Contains("no audio", info.Problem);
    }

    [Fact]
    public void Reports_a_missing_file_rather_than_throwing()
    {
        using var t = new TempPack();

        var info = WavInspector.Inspect(t.At("nothing.wav"));

        Assert.False(info.Ok);
        Assert.NotNull(info.Problem);
    }
}
