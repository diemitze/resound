using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>Source files are referenced in place and never copied, moved or deleted.</summary>
public class PackStagingTests : IDisposable
{
    private readonly TempPack _temp = new();
    private readonly PackStaging _staging;

    public PackStagingTests()
    {
        // No ffmpeg, the common case. Conversion is covered separately.
        var converter = new AudioConverter(new TestLogger<AudioConverter>()) { FfmpegOverride = "" };

        _staging = new PackStaging(converter, new TestLogger<PackStaging>())
        {
            Root = _temp.At("staging")
        };
    }

    public void Dispose() => _temp.Dispose();

    // ------------------------------------------------------------------ taking one file

    [Fact]
    public void A_playable_wav_is_used_where_it_lies()
    {
        var source = _temp.Wav("rips/shot.wav");

        var candidate = _staging.Take(source);

        Assert.True(candidate.Ok);
        Assert.Equal(source, candidate.File!.SourcePath);
        Assert.False(candidate.Converted);
        Assert.False(Directory.Exists(_temp.At("staging")));
    }

    [Fact]
    public void What_the_file_is_gets_reported_so_it_can_be_shown()
    {
        var candidate = _staging.Take(_temp.Wav("rips/shot.wav", channels: 2, sampleRate: 44100, seconds: 1.5));

        Assert.Equal(2, candidate.Channels);
        Assert.Equal(44100, candidate.SampleRate);
        Assert.Equal(1.5f, candidate.Seconds, 0.01f);
    }

    [Fact]
    public void The_slot_is_guessed_from_the_name_when_none_is_given()
    {
        Assert.Equal("tail", _staging.Take(_temp.Wav("rips/wpn_assault_dist_01.wav")).Slot);
        Assert.Equal("body", _staging.Take(_temp.Wav("rips/wpn_ak_shot_plr.wav")).Slot);
    }

    [Fact]
    public void An_explicit_slot_beats_the_guess()
    {
        Assert.Equal("body", _staging.Take(_temp.Wav("rips/wpn_assault_dist_01.wav"), "body").Slot);
    }

    [Fact]
    public void A_wav_the_plugin_cannot_load_is_refused_with_the_command_that_fixes_it()
    {
        var candidate = _staging.Take(_temp.Wav("rips/broken.wav", bits: 4));

        Assert.False(candidate.Ok);
        Assert.Contains("PCM 16-bit", candidate.Problem);
        Assert.StartsWith("ffmpeg -i", candidate.Fix);
    }

    [Fact]
    public void Without_ffmpeg_an_mp3_is_refused_and_says_how_to_get_ffmpeg()
    {
        var candidate = _staging.Take(_temp.File_("rips/shot.mp3"));

        Assert.False(candidate.Ok);
        Assert.Contains("only reads WAV", candidate.Problem);
        Assert.Contains("winget install", candidate.Fix);
    }

    [Fact]
    public void A_missing_file_is_reported_rather_than_throwing()
    {
        Assert.False(_staging.Take(_temp.At("rips", "gone.wav")).Ok);
    }

    [Fact]
    public void A_doubtful_name_carries_the_doubt_without_being_rejected()
    {
        var candidate = _staging.Take(_temp.Wav("rips/ak_foley_01.wav"));

        Assert.True(candidate.Ok);
        Assert.NotNull(candidate.Doubt);
    }

    [Fact]
    public void A_file_already_sorted_into_an_event_is_not_warned_about()
    {
        var candidate = _staging.Take(_temp.Wav("rips/ak_bolt_back.wav"));

        Assert.Equal("events/SndBoltOut", candidate.Slot);
        Assert.Null(candidate.Doubt);
    }

    // ------------------------------------------------------------------ scanning a folder

    [Fact]
    public void Scanning_finds_audio_all_the_way_down_and_sorts_it()
    {
        _temp.Wav("rips/wpn_ak_shot_plr.wav");
        _temp.Wav("rips/dist/wpn_assault_dist_01.wav");
        _temp.Wav("rips/deep/deeper/wpn_ak_sil_shot.wav");

        var found = _staging.Scan(_temp.At("rips"));

        Assert.Equal(3, found.Count);
        Assert.Contains(found, c => c.Slot == "body");
        Assert.Contains(found, c => c.Slot == "tail");
        Assert.Contains(found, c => c.Slot == "body_silenced");
    }

    [Fact]
    public void A_tree_already_in_slot_folders_keeps_its_layout()
    {
        _temp.Wav("rips/body/anything.wav");
        _temp.Wav("rips/tail/anything.wav");
        _temp.Wav("rips/events/SndMagOut/anything.wav");

        var found = _staging.Scan(_temp.At("rips"));

        Assert.Contains(found, c => c.Slot == "body");
        Assert.Contains(found, c => c.Slot == "tail");
        Assert.Contains(found, c => c.Slot == "events/SndMagOut");
    }

    [Fact]
    public void Forcing_a_slot_overrides_every_guess_in_the_folder()
    {
        _temp.Wav("rips/wpn_ak_shot_plr.wav");
        _temp.Wav("rips/wpn_assault_dist_01.wav");

        var found = _staging.Scan(_temp.At("rips"), "tail");

        Assert.All(found, c => Assert.Equal("tail", c.Slot));
    }

    [Fact]
    public void Files_that_are_not_audio_are_ignored_entirely()
    {
        _temp.Wav("rips/shot.wav");
        _temp.File_("rips/readme.txt");
        _temp.File_("rips/dumper.log");

        Assert.Single(_staging.Scan(_temp.At("rips")));
    }

    [Fact]
    public void A_folder_with_no_audio_says_so_rather_than_returning_nothing()
    {
        _temp.File_("rips/readme.txt");

        var found = _staging.Scan(_temp.At("rips"));

        Assert.Single(found);
        Assert.False(found[0].Ok);
        Assert.Contains("no audio", found[0].Problem);
    }

    [Fact]
    public void A_folder_that_is_not_there_says_so_and_suggests_pasting_the_path()
    {
        var found = _staging.Scan(_temp.At("nope"));

        Assert.Single(found);
        Assert.Contains("no folder there", found[0].Problem);
        Assert.Contains("Explorer", found[0].Fix);
    }

    [Fact]
    public void Unusable_files_come_back_alongside_usable_ones_rather_than_being_dropped()
    {
        _temp.Wav("rips/good.wav");
        _temp.Wav("rips/broken.wav", bits: 4);
        _temp.File_("rips/needs_ffmpeg.mp3");

        var found = _staging.Scan(_temp.At("rips"));

        Assert.Equal(3, found.Count);
        Assert.Single(found, c => c.Ok);
        Assert.Equal(2, found.Count(c => !c.Ok));
    }

    // ------------------------------------------------------------------ uploads

    [Fact]
    public async Task An_uploaded_wav_is_written_down_and_kept()
    {
        var bytes = File.ReadAllBytes(_temp.Wav("source/shot.wav"));

        var candidate = await _staging.TakeAsync(new MemoryStream(bytes), "shot.wav");

        Assert.True(candidate.Ok);
        Assert.True(File.Exists(candidate.File!.SourcePath));
        Assert.Equal("shot.wav", candidate.OriginalName);
    }

    [Fact]
    public async Task An_uploaded_file_that_is_not_audio_is_refused()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("this is not a wav");

        var candidate = await _staging.TakeAsync(new MemoryStream(bytes), "shot.wav");

        Assert.False(candidate.Ok);
    }

    // ------------------------------------------------------------------ playback and cleanup

    [Fact]
    public void A_staged_file_can_be_read_back_for_playing()
    {
        var candidate = _staging.Take(_temp.Wav("rips/shot.wav"));

        Assert.NotNull(_staging.Read(candidate.File!));
    }

    [Fact]
    public void Something_too_big_to_play_in_a_browser_is_refused_rather_than_sent()
    {
        var candidate = _staging.Take(_temp.Wav("rips/shot.wav"));

        Assert.Null(_staging.Read(candidate.File!, maxBytes: 8));
    }

    [Fact]
    public void Discarding_never_touches_a_file_that_was_referenced_where_it_lay()
    {
        var source = _temp.Wav("rips/shot.wav");
        var candidate = _staging.Take(source);

        _staging.Discard(candidate.File);

        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Discarding_removes_a_copy_that_staging_made()
    {
        var bytes = File.ReadAllBytes(_temp.Wav("source/shot.wav"));
        var candidate = await _staging.TakeAsync(new MemoryStream(bytes), "shot.wav");
        var staged = candidate.File!.SourcePath;

        _staging.Discard(candidate.File);

        Assert.False(File.Exists(staged));
    }

    [Fact]
    public async Task Clearing_empties_staging_and_leaves_the_originals_alone()
    {
        var source = _temp.Wav("rips/shot.wav");
        _staging.Take(source);

        var bytes = File.ReadAllBytes(source);
        await _staging.TakeAsync(new MemoryStream(bytes), "uploaded.wav");

        _staging.Clear();

        Assert.False(Directory.Exists(_temp.At("staging")));
        Assert.True(File.Exists(source));
    }
}
