using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>Slot guesses against real rip names. A shot guessed as tail plays as a distant crack.</summary>
public class SlotClassifierTests
{
    [Theory]
    // Black Ops II, which is what the shipped sounds came out of.
    [InlineData("wpn_ak47_shot_plr.wav", "body")]
    [InlineData("wpn_assault_dist_01.wav", "tail")]
    [InlineData("wpn_pistol_dist_03.wav", "tail")]
    [InlineData("wpn_m4_fire_plr.wav", "body")]
    [InlineData("wpn_smg_sil_shot_plr.wav", "body_silenced")]
    [InlineData("wpn_smg_silenced_dist.wav", "tail_silenced")]
    // CS2 / Source 2.
    [InlineData("ak47_01.wav", "body")]
    [InlineData("ak47-1-distant.wav", "tail")]
    [InlineData("deagle_suppressed_01.wav", "body_silenced")]
    // The odd ones.
    [InlineData("m4_doublet.wav", "doublet")]
    [InlineData("m4_doublet_sil.wav", "doublet_silenced")]
    [InlineData("shot_decay.wav", "tail")]
    [InlineData("anything at all.wav", "body")]
    public void Names_land_in_the_right_slot(string file, string slot)
    {
        Assert.Equal(slot, SlotClassifier.FromName(file));
    }

    [Fact]
    public void An_extension_never_changes_the_guess()
    {
        Assert.Equal("tail", SlotClassifier.FromName("wpn_dist_01.mp3"));
        Assert.Equal("tail", SlotClassifier.FromName("wpn_dist_01.ogg"));
    }

    [Theory]
    [InlineData("body/shot.wav", "body")]
    [InlineData("tail/anything.wav", "tail")]
    [InlineData("body_silenced/x.wav", "body_silenced")]
    [InlineData("Tail/MixedCase.wav", "tail")]
    [InlineData("myset/tail/shot.wav", "tail")]
    public void A_tree_already_laid_out_in_slots_keeps_its_layout(string path, string slot)
    {
        Assert.Equal(slot, SlotClassifier.FromPath(path));
    }

    [Theory]
    [InlineData("events/SndMagOut/mag.wav", "events/SndMagOut")]
    [InlineData("myset/events/SndBoltIn/bolt.wav", "events/SndBoltIn")]
    public void Event_folders_are_honoured_because_the_folder_name_is_the_event(string path, string slot)
    {
        Assert.Equal(slot, SlotClassifier.FromPath(path));
    }

    [Fact]
    public void A_folder_that_means_nothing_falls_back_to_the_filename()
    {
        Assert.Equal("tail", SlotClassifier.FromPath("rips/bo2/cmn_root/wpn_assault_dist_01.wav"));
        Assert.Equal("body", SlotClassifier.FromPath("rips/bo2/ak47_shot.wav"));
    }

    [Fact]
    public void Backslashes_work_the_same_as_forward_ones()
    {
        Assert.Equal("tail", SlotClassifier.FromPath(@"myset\tail\shot.wav"));
    }

    [Fact]
    public void A_bare_filename_is_still_classified()
    {
        Assert.Equal("body", SlotClassifier.FromPath("shot.wav"));
    }

    [Theory]
    [InlineData("wpn_ak_foley_01.wav")]
    [InlineData("ak_dryfire.wav")]
    [InlineData("shell_drop_01.wav")]
    [InlineData("wpn_assault_lfe.wav")]
    [InlineData("ak_mag_out.wav")]
    public void Handling_sounds_are_flagged_rather_than_dropped(string file)
    {
        Assert.NotNull(SlotClassifier.Doubt(file));
    }

    [Fact]
    public void A_third_person_take_is_flagged_because_one_set_is_heard_by_everybody()
    {
        Assert.Contains("third-person", SlotClassifier.Doubt("wpn_ak47_shot_npc.wav"));
    }

    [Fact]
    public void An_ordinary_gunshot_has_nothing_said_about_it()
    {
        Assert.Null(SlotClassifier.Doubt("wpn_ak47_shot_plr.wav"));
        Assert.Null(SlotClassifier.Doubt("ak47-1-distant.wav"));
    }

    [Theory]
    // The Rust rips that went in as gunshots before this existed.
    [InlineData("rust-ak74u-bolt-back.wav", "events/SndBoltOut")]
    [InlineData("rust-ak74u-bolt-forward.wav", "events/SndBoltIn")]
    [InlineData("rust-ak74u-deploy.wav", "events/SndDraw")]
    [InlineData("rust-ak74u-grab-magazine.wav", "events/SndMagPullOut")]
    [InlineData("rust-ak74u-insert-magazine.wav", "events/SndMagIn")]
    [InlineData("ak_mag_out.wav", "events/SndMagOut")]
    [InlineData("ak_dryfire.wav", "events/SndDryFire")]
    [InlineData("glock_slide_back.wav", "events/SndSlideOut")]
    [InlineData("m870_pump_forward.wav", "events/SndPumpForward")]
    public void Handling_names_go_to_their_event_rather_than_body(string file, string slot)
    {
        Assert.Equal(slot, SlotClassifier.FromName(file));
    }

    [Theory]
    [InlineData("rust-ak74u-attack.wav", "body")]
    [InlineData("rust-ak74u-attack-silenced.wav", "body_silenced")]
    [InlineData("rust-ak74u-decay.wav", "tail")]
    [InlineData("44_magnum_shot.wav", "body")]
    public void Shots_that_share_words_with_handling_stay_shots(string file, string slot)
    {
        Assert.Equal(slot, SlotClassifier.FromName(file));
    }
}
