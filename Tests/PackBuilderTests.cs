using ResoundServer.Models;
using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>Mostly round-trips through PackManifest: whatever the builder writes must read back unchanged.</summary>
public class PackBuilderTests
{
    private static PackBuilderService Builder(TempPack t)
    {
        return new PackBuilderService(new PackValidator(), new TestLogger<PackBuilderService>())
        {
            InstallRoot = t.At("plugins", "Resound")
        };
    }

    private static SetDraft Set(string name, string soundSet, Action<PackRule>? tweak = null)
    {
        var rule = new PackRule { Name = name, SoundSet = soundSet, WeapClass = "smg" };
        tweak?.Invoke(rule);

        return new SetDraft { Rule = rule };
    }

    private static PackDraft Draft(params SetDraft[] sets)
    {
        return new PackDraft { Id = "mypack", Name = "My Pack", Author = "me", Sets = [.. sets] };
    }

    private static PackRow ReadBack(BuildResult result)
    {
        return PackManifest.Read(Path.Join(result.Directory, PackManifest.FileName));
    }

    // ------------------------------------------------------------------ writing

    [Fact]
    public void Writes_a_manifest_and_the_slot_folders()
    {
        using var t = new TempPack();

        var result = Builder(t).Build(Draft(Set("SMG", "mysmg")));

        Assert.True(result.Written);
        Assert.True(File.Exists(Path.Join(result.Directory, PackManifest.FileName)));
        Assert.True(Directory.Exists(Path.Join(result.Directory, "sounds", "mysmg", "body")));
        Assert.True(Directory.Exists(Path.Join(result.Directory, "sounds", "mysmg", "doublet_silenced")));
    }

    [Fact]
    public void Copies_staged_audio_into_the_slot_it_was_given()
    {
        using var t = new TempPack();
        var source = t.Wav("rips/shot.wav");
        var event_ = t.Wav("rips/mag.wav");

        var set = Set("SMG", "mysmg");
        set.Files =
        [
            new StagedFile { Slot = "body", SourcePath = source },
            new StagedFile { Slot = "events/SndMagOut", SourcePath = event_ }
        ];

        var result = Builder(t).Build(Draft(set));

        Assert.Equal(2, result.FilesCopied);
        Assert.True(File.Exists(Path.Join(result.Directory, "sounds", "mysmg", "body", "shot.wav")));
        Assert.True(File.Exists(Path.Join(result.Directory, "sounds", "mysmg", "events", "SndMagOut", "mag.wav")));
    }

    [Fact]
    public void Renames_a_staged_file_when_asked_to()
    {
        using var t = new TempPack();

        var set = Set("SMG", "mysmg");
        set.Files = [new StagedFile { Slot = "body", SourcePath = t.Wav("rips/ugly name 01.wav"), FileName = "shot.wav" }];

        var result = Builder(t).Build(Draft(set));

        Assert.True(File.Exists(Path.Join(result.Directory, "sounds", "mysmg", "body", "shot.wav")));
    }

    [Fact]
    public void Checks_the_pack_it_just_wrote()
    {
        using var t = new TempPack();

        var set = Set("SMG", "mysmg");
        set.Files = [new StagedFile { Slot = "body", SourcePath = t.Wav("rips/shot.wav") }];

        var health = Builder(t).Build(Draft(set)).Health;

        Assert.NotNull(health);
        Assert.True(health.Healthy);
    }

    [Fact]
    public void A_set_with_no_audio_is_written_but_reported_as_silent()
    {
        using var t = new TempPack();

        var result = Builder(t).Build(Draft(Set("SMG", "mysmg")));

        Assert.True(result.Written);
        Assert.Contains(result.Health!.Problems, p => p.Message.Contains("no usable audio"));
    }

    [Fact]
    public void An_overwrite_leaves_files_it_did_not_write_alone()
    {
        using var t = new TempPack();
        var builder = Builder(t);

        var first = builder.Build(Draft(Set("SMG", "mysmg")));
        var stale = Path.Join(first.Directory, "sounds", "mysmg", "body", "old.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.Copy(t.Wav("rips/shot.wav"), stale);

        var second = builder.Build(Draft(Set("SMG", "mysmg")), overwrite: true);

        Assert.True(second.Written);
        Assert.True(File.Exists(stale));
    }

    // ------------------------------------------------------------------ round-trip

    [Fact]
    public void Pack_fields_round_trip_through_the_reader()
    {
        using var t = new TempPack();

        var draft = Draft(Set("SMG", "mysmg"));
        draft.Priority = -50;
        draft.AutoLevel = false;

        var pack = ReadBack(Builder(t).Build(draft));

        Assert.Equal("mypack", pack.Id);
        Assert.Equal("My Pack", pack.Name);
        Assert.Equal("me", pack.Author);
        Assert.Equal(-50, pack.Priority);
        Assert.False(pack.AutoLevel);
    }

    [Fact]
    public void Every_match_type_round_trips()
    {
        using var t = new TempPack();

        var draft = Draft(
            Set("Exact", "a", r => { r.WeapClass = null; r.TemplateId = "6183afd850224f204c1da514"; }),
            Set("Several", "b", r => { r.WeapClass = null; r.TemplateIds = ["aaa", "bbb"]; }),
            Set("Category", "c", r => { r.WeapClass = null; r.ParentId = "5447b5f14bdc2d61278b4567"; }),
            Set("Class and calibre", "d", r => r.Caliber = "Caliber762x51"),
            Set("By name", "e", r => { r.WeapClass = null; r.NamePattern = "weapon_.*ak(74|103)"; }));

        var rules = ReadBack(Builder(t).Build(draft)).Rules;

        Assert.Equal("6183afd850224f204c1da514", rules[0].TemplateId);
        Assert.Equal(["aaa", "bbb"], rules[1].TemplateIds);
        Assert.Equal("5447b5f14bdc2d61278b4567", rules[2].ParentId);
        Assert.Equal("smg", rules[3].WeapClass);
        Assert.Equal("Caliber762x51", rules[3].Caliber);
        Assert.Equal("weapon_.*ak(74|103)", rules[4].NamePattern);
    }

    [Fact]
    public void Specificity_survives_the_round_trip_because_it_decides_which_pack_wins()
    {
        using var t = new TempPack();

        var draft = Draft(
            Set("Exact", "a", r => { r.WeapClass = null; r.TemplateId = "abc"; }),
            Set("Class and calibre", "b", r => r.Caliber = "Caliber9x19PARA"),
            Set("Class", "c"));

        var rules = ReadBack(Builder(t).Build(draft)).Rules;

        Assert.Equal(100, rules[0].Specificity);
        Assert.Equal(70, rules[1].Specificity);
        Assert.Equal(40, rules[2].Specificity);
    }

    [Fact]
    public void Volumes_round_trip_and_the_ones_left_alone_read_back_as_one()
    {
        using var t = new TempPack();

        var draft = Draft(Set("SMG", "mysmg", r =>
        {
            r.TailVolume = 0.3f;
            r.BodySilencedVolume = 0.25f;
            r.DecayBeats = 8f;
        }));

        var rule = ReadBack(Builder(t).Build(draft)).Rules.Single();

        Assert.Equal(0.3f, rule.TailVolume, 0.0001f);
        Assert.Equal(0.25f, rule.BodySilencedVolume!.Value, 0.0001f);
        Assert.Equal(8f, rule.DecayBeats);
        Assert.Equal(1f, rule.BodyVolume, 0.0001f);
        Assert.Null(rule.TailSilencedVolume);
    }

    [Fact]
    public void A_disabled_entry_round_trips()
    {
        using var t = new TempPack();

        var draft = Draft(Set("SMG", "mysmg", r => r.Enabled = false));

        Assert.False(ReadBack(Builder(t).Build(draft)).Rules.Single().Enabled);
    }

    [Fact]
    public void Auto_level_is_inherited_from_the_pack_unless_the_entry_overrides_it()
    {
        using var t = new TempPack();

        var overridden = Set("Off", "a");
        overridden.AutoLevel = false;

        var draft = Draft(Set("Inherits", "b"), overridden);
        draft.AutoLevel = true;

        var rules = ReadBack(Builder(t).Build(draft)).Rules;

        Assert.True(rules[0].AutoLevel);
        Assert.False(rules[1].AutoLevel);
    }

    [Fact]
    public void Default_fields_are_left_out_so_the_manifest_stays_readable()
    {
        using var t = new TempPack();

        var json = Builder(t).Manifest(Draft(Set("SMG", "mysmg")));

        Assert.DoesNotContain("bodyVolume", json);
        Assert.DoesNotContain("decayBeats", json);
        Assert.DoesNotContain("priority", json);
        Assert.DoesNotContain("enabled", json);
        Assert.Contains("\"apiVersion\": 1", json);
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void Refuses_a_pack_with_no_id()
    {
        using var t = new TempPack();

        var draft = Draft(Set("SMG", "mysmg"));
        draft.Id = "";

        var result = Builder(t).Build(draft);

        Assert.False(result.Written);
        Assert.Contains(result.Refusals, p => p.Message.Contains("needs an id"));
    }

    [Fact]
    public void Refuses_a_pack_with_no_sets()
    {
        using var t = new TempPack();

        Assert.Contains(Builder(t).Check(Draft()), p => p.Message.Contains("no sound sets"));
    }

    [Fact]
    public void Refuses_a_set_that_claims_no_weapons()
    {
        using var t = new TempPack();

        var refusals = Builder(t).Check(Draft(Set("SMG", "mysmg", r => r.WeapClass = null)));

        Assert.Contains(refusals, p => p.Message.Contains("claims no weapons"));
    }

    [Fact]
    public void Refuses_to_overwrite_an_installed_pack_unless_told_to()
    {
        using var t = new TempPack();
        var builder = Builder(t);

        builder.Build(Draft(Set("SMG", "mysmg")));

        Assert.Contains(builder.Check(Draft(Set("SMG", "mysmg"))), p => p.Message.Contains("already installed"));
        Assert.Empty(builder.Check(Draft(Set("SMG", "mysmg")), overwrite: true));
    }

    [Fact]
    public void Refuses_a_file_that_is_not_a_wav_and_says_how_to_convert_it()
    {
        using var t = new TempPack();

        var set = Set("SMG", "mysmg");
        set.Files = [new StagedFile { Slot = "body", SourcePath = t.File_("rips/shot.mp3") }];

        var refusal = Builder(t).Check(Draft(set)).Single();

        Assert.Contains("only reads WAV", refusal.Message);
        Assert.StartsWith("ffmpeg -i", refusal.Fix);
    }

    [Fact]
    public void Refuses_a_wav_the_plugin_could_not_load()
    {
        using var t = new TempPack();

        var set = Set("SMG", "mysmg");
        set.Files = [new StagedFile { Slot = "body", SourcePath = t.Wav("rips/shot.wav", bits: 4) }];

        Assert.Contains(Builder(t).Check(Draft(set)), p => p.Message.Contains("PCM 16-bit"));
    }

    [Fact]
    public void Refuses_a_file_that_has_gone_missing_since_it_was_staged()
    {
        using var t = new TempPack();

        var set = Set("SMG", "mysmg");
        set.Files = [new StagedFile { Slot = "body", SourcePath = t.At("rips", "gone.wav") }];

        Assert.Contains(Builder(t).Check(Draft(set)), p => p.Message.Contains("not there any more"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("events/../../escape")]
    [InlineData("events/")]
    [InlineData("nonsense")]
    public void Refuses_a_slot_that_is_not_one(string slot)
    {
        using var t = new TempPack();

        var set = Set("SMG", "mysmg");
        set.Files = [new StagedFile { Slot = slot, SourcePath = t.Wav("rips/shot.wav") }];

        Assert.Contains(Builder(t).Check(Draft(set)), p => p.Message.Contains("is not a slot"));
    }

    // ------------------------------------------------------------------ slugs

    [Theory]
    [InlineData("My  Great Pack!!", "my-great-pack")]
    [InlineData("ak_74n", "ak_74n")]
    [InlineData("CS:GO Guns", "cs-go-guns")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("---", "")]
    [InlineData(null, "")]
    public void Ids_and_folder_names_are_slugified(string? input, string expected)
    {
        Assert.Equal(expected, PackBuilderService.Slug(input));
    }

    [Fact]
    public void A_pack_id_with_spaces_becomes_the_folder_people_install()
    {
        using var t = new TempPack();

        var draft = Draft(Set("SMG", "mysmg"));
        draft.Id = "My Pack";

        var result = Builder(t).Build(draft);

        Assert.Equal("my-pack", Path.GetFileName(result.Directory));
        Assert.Equal("my-pack", ReadBack(result).Id);
    }

    // ------------------------------------------------------------------ claims

    [Fact]
    public void Claims_lists_the_weapons_an_entry_would_take()
    {
        List<WeaponRow> weapons =
        [
            new() { Id = "1", Name = "MP5", WeapClass = "smg", Caliber = "Caliber9x19PARA", InternalName = "weapon_hk_mp5" },
            new() { Id = "2", Name = "MP7", WeapClass = "smg", Caliber = "Caliber46x30", InternalName = "weapon_hk_mp7" },
            new() { Id = "3", Name = "AK-74N", WeapClass = "assaultRifle", Caliber = "Caliber545x39", InternalName = "weapon_izhmash_ak74n" }
        ];

        Assert.Equal(2, PackBuilderService.Claims(new PackRule { WeapClass = "smg" }, weapons).Count);
        Assert.Single(PackBuilderService.Claims(new PackRule { NamePattern = "ak74" }, weapons));
        Assert.Empty(PackBuilderService.Claims(new PackRule(), weapons));
    }
}
