using ResoundServer.Models;
using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>Each problem is a weapon silently staying vanilla. The messages are part of the contract.</summary>
public class PackValidatorTests
{
    private static readonly PackValidator Validator = new();

    private static PackRow Pack(string root, params PackRule[] rules)
    {
        return new PackRow { Id = "fixture", Name = "Fixture", Directory = root, Rules = [.. rules] };
    }

    private static PackRule Rule(string name, string soundSet, bool enabled = true)
    {
        return new PackRule { Name = name, SoundSet = soundSet, WeapClass = "smg", Enabled = enabled };
    }

    private static bool Has(PackHealth h, string entry, string? where, string contains)
    {
        return h.Problems.Any(p => p.Entry == entry && p.Where == where && p.Message.Contains(contains));
    }

    [Fact]
    public void A_set_with_audio_in_every_slot_is_clean()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.Wav("sounds/mysmg/tail/dist.wav");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.True(health.Healthy);
        Assert.Equal(2, health.Clips);
    }

    [Fact]
    public void Loose_wavs_in_the_set_folder_count_as_the_body()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/shot.wav");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.True(health.Healthy);
        Assert.Equal(1, health.Clips);
    }

    [Fact]
    public void An_empty_slot_folder_is_fine_because_it_keeps_the_vanilla_bank()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.Dir("sounds/mysmg/tail");

        Assert.True(Validator.Check(Pack(t.Root, Rule("SMG", "mysmg"))).Healthy);
    }

    [Fact]
    public void An_empty_event_folder_is_not_fine_because_nobody_makes_one_on_purpose()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.Dir("sounds/mysmg/events/SndMagIn");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.True(Has(health, "SMG", "events/SndMagIn", "no audio files"));
    }

    [Fact]
    public void A_set_with_no_audio_keeps_the_weapon_vanilla()
    {
        using var t = new TempPack();
        t.Dir("sounds/mysmg");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.True(Has(health, "SMG", null, "no usable audio"));
    }

    [Fact]
    public void A_misspelled_set_name_says_what_it_probably_meant()
    {
        using var t = new TempPack();
        t.Wav("sounds/my_smg/body/shot.wav");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        var problem = health.Problems.Single(p => p.Entry == "SMG" && p.Message.Contains("no folder"));
        Assert.Contains("my_smg", problem.Fix);
    }

    [Fact]
    public void A_non_wav_file_is_reported_with_the_command_that_converts_it()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.File_("sounds/mysmg/body/other.mp3");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        var problem = health.Problems.Single(p => p.Where == "body/other.mp3");
        Assert.Contains("only reads WAV", problem.Message);
        Assert.StartsWith("ffmpeg -i", problem.Fix);
    }

    [Fact]
    public void Files_that_are_not_audio_at_all_are_left_alone()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.File_("sounds/mysmg/body/readme.txt", "where these came from");
        t.File_("sounds/mysmg/body/LICENSE");

        Assert.True(Validator.Check(Pack(t.Root, Rule("SMG", "mysmg"))).Healthy);
    }

    [Fact]
    public void A_slot_where_nothing_loads_says_it_stays_vanilla()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.Wav("sounds/mysmg/tail/broken.wav", bits: 4);

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.True(Has(health, "SMG", "tail", "keeps its vanilla sound"));
    }

    [Fact]
    public void Stereo_is_a_note_not_a_problem()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav", channels: 2);

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.True(health.Healthy);
        Assert.Contains(health.Notes, n => n.Where == "body/shot.wav");
    }

    [Fact]
    public void An_entry_that_claims_no_weapons_is_a_problem()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");

        var health = Validator.Check(Pack(t.Root, new PackRule { Name = "SMG", SoundSet = "mysmg" }));

        Assert.True(Has(health, "SMG", null, "claims no weapons"));
    }

    [Fact]
    public void An_entry_with_no_sound_set_is_a_problem()
    {
        using var t = new TempPack();
        t.Dir("sounds");

        var health = Validator.Check(Pack(t.Root, new PackRule { Name = "SMG", WeapClass = "smg" }));

        Assert.True(Has(health, "SMG", null, "no soundSet"));
    }

    [Fact]
    public void A_set_folder_no_entry_uses_is_reported_because_it_is_invisible_otherwise()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.Wav("sounds/leftover/body/shot.wav");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.Contains(health.Problems, p => p.Message.Contains("leftover"));
    }

    [Fact]
    public void A_disabled_entry_still_claims_its_folder_so_parking_work_does_not_look_broken()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/shot.wav");
        t.Wav("sounds/parked/body/shot.wav");

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg"), Rule("Parked", "parked", enabled: false)));

        Assert.True(health.Healthy);
        Assert.Contains(health.Notes, n => n.Entry == "Parked");
    }

    [Fact]
    public void A_pack_with_no_sounds_folder_says_so_and_stops()
    {
        using var t = new TempPack();

        var health = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg")));

        Assert.Single(health.Problems);
        Assert.Contains("no 'sounds' folder", health.Problems.First().Message);
    }

    [Fact]
    public void Slot_counts_are_reported_per_set()
    {
        using var t = new TempPack();
        t.Wav("sounds/mysmg/body/one.wav");
        t.Wav("sounds/mysmg/body/two.wav");
        t.Wav("sounds/mysmg/tail/dist.wav");
        t.Wav("sounds/mysmg/events/SndMagOut/mag.wav");

        var set = Validator.Check(Pack(t.Root, Rule("SMG", "mysmg"))).Sets.Single();

        Assert.Equal(4, set.Clips);
        Assert.Equal(2, set.Slots.Single(s => s.Slot == "body").Clips);
        Assert.Equal(1, set.Slots.Single(s => s.Slot == "events/SndMagOut").Clips);
    }
}
