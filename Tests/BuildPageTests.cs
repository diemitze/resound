using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ResoundServer.Services;
using ResoundServer.Web.Pages;

namespace ResoundServer.Tests;

/// <summary>The builder page rendered. Its logic is tested in SetEditorTests; this covers what only a render reaches.</summary>
public class BuildPageTests : BunitContext
{
    private readonly TempPack _temp = new();

    public BuildPageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(FakeSpt.Service(_temp.At("plugins")));
        Services.AddSingleton(new PackValidator());
        Services.AddSingleton(new AudioConverter(new TestLogger<AudioConverter>()) { FfmpegOverride = "" });
        Services.AddSingleton(sp => new PackStaging(
            sp.GetRequiredService<AudioConverter>(), new TestLogger<PackStaging>())
        {
            Root = _temp.At("staging")
        });
        Services.AddSingleton(sp => new PackBuilderService(
            sp.GetRequiredService<PackValidator>(), new TestLogger<PackBuilderService>())
        {
            InstallRoot = _temp.At("plugins", "Resound")
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<Build> Page() => Render<Build>();

    private static void Click(IRenderedComponent<Build> page, string text)
    {
        page.FindAll("button").First(b => b.TextContent.Contains(text)).Click();
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Build> page, string text)
    {
        return page.FindAll("button").First(b => b.TextContent.Contains(text));
    }

    /// <summary>An icon button, found by a fragment of the icon's own path data.</summary>
    private static void ClickIcon(IRenderedComponent<Build> page, string pathFragment)
    {
        page.FindAll(".mud-icon-button").First(b => b.OuterHtml.Contains(pathFragment)).Click();
    }

    private const string TrashIcon = "M16 9v10H8V9h8m-1.5-6h-5";

    /// <summary>Presses Build, and confirms if the page asks about a set with no audio.</summary>
    private static void BuildPack(IRenderedComponent<Build> page)
    {
        Click(page, "Build the pack");

        if (page.Markup.Contains("Build it anyway"))
            Click(page, "Build it anyway");
    }

    [Fact]
    public void Renders_with_nothing_filled_in()
    {
        var page = Page();

        Assert.Contains("Make a sound pack", page.Markup);
        Assert.Contains("Nothing in this pack yet", page.Markup);
    }

    [Fact]
    public void Adding_a_set_names_it_and_shows_what_it_claims()
    {
        var page = Page();
        Click(page, "Add a sound set");

        // The default class is whichever sorts first, which with this fake is assaultRifle.
        Assert.Contains("Every assault rifle", page.Markup);
        Assert.Contains("<b>2</b> weapons", page.Markup);
        Assert.Contains("AK74N", page.Markup);
        Assert.Contains("SA58", page.Markup);
        Assert.DoesNotContain("MP5", page.Markup);
    }

    [Fact]
    public void A_set_can_be_removed_again()
    {
        var page = Page();
        Click(page, "Add a sound set");
        Assert.DoesNotContain("Nothing in this pack yet", page.Markup);

        ClickIcon(page, TrashIcon);

        Assert.Contains("Nothing in this pack yet", page.Markup);
    }

    [Fact]
    public void Switching_a_set_to_picking_weapons_shows_the_picker_and_claims_nothing_yet()
    {
        var page = Page();
        Click(page, "Add a sound set");

        page.FindAll(".mud-chip").First(c => c.TextContent.Contains("these weapons")).Click();

        Assert.Contains("selected of 5", page.Markup);
        Assert.Contains("claims no weapons", page.Markup);
    }

    [Fact]
    public void The_more_options_disclosure_opens_and_closes()
    {
        var page = Page();

        Assert.DoesNotContain("Match my clips", page.Markup);

        Click(page, "More options");
        Assert.Contains("Match my clips", page.Markup);

        Click(page, "Fewer options");
        Assert.DoesNotContain("Match my clips", page.Markup);
    }

    [Fact]
    public void The_install_path_appears_as_soon_as_there_is_an_id()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("My Pack");

        Assert.Contains("my-pack", page.Markup);
    }

    [Fact]
    public void Building_is_held_back_until_the_pack_has_a_name()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Assert.Contains("Give the pack a name first", page.Markup);
        Assert.Contains("disabled", Button(page, "Build the pack").OuterHtml);
    }

    [Fact]
    public void Building_without_audio_writes_the_pack_and_says_where_to_put_it()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        BuildPack(page);

        Assert.Contains("no audio yet, so these weapons stay vanilla", page.Markup);

        var manifest = Path.Join(_temp.At("plugins", "Resound"), "mypack", PackManifest.FileName);
        Assert.True(File.Exists(manifest));

        // What was written has to be what the page said it claimed.
        var pack = PackManifest.Read(manifest);
        Assert.Equal("assaultRifle", pack.Rules.Single().WeapClass);
        Assert.Equal("assaultrifle", pack.Rules.Single().SoundSet);
        Assert.True(Directory.Exists(Path.Join(Path.GetDirectoryName(manifest)!, "sounds", "assaultrifle", "body")));
    }

    [Fact]
    public void Building_twice_overwrites_rather_than_refusing_the_second_press()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        BuildPack(page);
        BuildPack(page);

        Assert.DoesNotContain("Nothing was written", page.Markup);
        Assert.Contains("no audio yet, so these weapons stay vanilla", page.Markup);
    }


    // ------------------------------------------------------------------ audio

    /// <summary>The input behind a label. Indexing FindAll("input") breaks when a field is added.</summary>
    private static AngleSharp.Dom.IElement Field(IRenderedComponent<Build> page, string label)
    {
        var id = page.FindAll("label")
            .First(l => l.TextContent.Trim() == label)
            .GetAttribute("for");

        return page.FindAll("input").First(i => i.GetAttribute("id") == id);
    }

    /// <summary>Puts a folder of rips on disk and points the set's scan box at it.</summary>
    private void Scan(IRenderedComponent<Build> page, params string[] files)
    {
        foreach (var file in files)
        {
            if (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                _temp.Wav("rips/" + file);
            else
                _temp.File_("rips/" + file);
        }

        Field(page, "Folder of rips").Input(_temp.At("rips"));
        Click(page, "Scan");
    }

    [Fact]
    public void A_new_set_says_it_has_no_audio_and_what_that_means()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Assert.Contains("nothing yet, so these weapons stay vanilla", page.Markup);
    }

    [Fact]
    public void Scanning_a_folder_sorts_the_rips_into_slots()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Scan(page, "wpn_ak_shot_plr.wav", "wpn_assault_dist_01.wav", "wpn_ak_sil_shot.wav");

        Assert.Contains("<b>3</b> clips", page.Markup);
        Assert.Contains("body, body_silenced, tail", page.Markup);
        Assert.Contains("wpn_assault_dist_01.wav", page.Markup);
    }

    [Fact]
    public void Audio_with_no_body_clip_says_the_gunshot_is_unchanged()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Scan(page, "wpn_assault_dist_01.wav");

        Assert.Contains("nothing in body/", page.Markup);
    }

    [Fact]
    public void A_file_the_plugin_cannot_load_is_listed_with_the_reason_rather_than_dropped()
    {
        var page = Page();
        Click(page, "Add a sound set");

        _temp.Wav("rips/good.wav");
        _temp.File_("rips/needs_ffmpeg.mp3");
        Field(page, "Folder of rips").Input(_temp.At("rips"));
        Click(page, "Scan");

        Assert.Contains("needs_ffmpeg.mp3", page.Markup);
        Assert.Contains("only reads WAV", page.Markup);
        Assert.Contains("<b>1</b> clip", page.Markup);
    }

    [Fact]
    public void A_handling_sound_is_flagged_but_still_taken()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Scan(page, "wpn_ak_foley_01.wav");

        Assert.Contains("<b>1</b> clip", page.Markup);
        Assert.Contains("looks like handling", page.Markup);
    }

    [Fact]
    public void Scanning_a_folder_that_is_not_there_says_so()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Field(page, "Folder of rips").Input(_temp.At("nowhere"));
        Click(page, "Scan");

        Assert.Contains("no folder there", page.Markup);
    }

    [Fact]
    public void Scanning_the_same_folder_twice_does_not_double_the_clips()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Scan(page, "wpn_ak_shot_plr.wav");
        Click(page, "Scan");

        Assert.Contains("<b>1</b> clip", page.Markup);
    }

    [Fact]
    public void A_clip_can_be_taken_back_out()
    {
        var page = Page();
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav");

        page.FindAll(".mud-icon-button").Last().Click();

        Assert.Contains("nothing yet", page.Markup);
    }

    [Fact]
    public void Building_writes_the_audio_into_the_slot_folders()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav", "wpn_assault_dist_01.wav");
        BuildPack(page);

        var pack = Path.Join(_temp.At("plugins", "Resound"), "mypack", "sounds", "assaultrifle");

        Assert.True(File.Exists(Path.Join(pack, "body", "wpn_ak_shot_plr.wav")));
        Assert.True(File.Exists(Path.Join(pack, "tail", "wpn_assault_dist_01.wav")));
    }

    [Fact]
    public void A_built_pack_with_audio_passes_its_own_health_check()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav");
        BuildPack(page);

        Assert.DoesNotContain("PROBLEM", page.Markup);
        Assert.Contains("1 clip(s) in body", page.Markup);
        Assert.DoesNotContain("no audio yet", page.Markup);
    }

    [Fact]
    public void Scanning_never_moves_or_copies_the_originals()
    {
        var page = Page();
        Click(page, "Add a sound set");

        var source = _temp.Wav("rips/wpn_ak_shot_plr.wav");
        Field(page, "Folder of rips").Input(_temp.At("rips"));
        Click(page, "Scan");

        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(_temp.At("staging")));
    }

    [Fact]
    public void Without_ffmpeg_the_page_says_so_and_how_to_change_it()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Assert.Contains("WAV only on this machine", page.Markup);
        Assert.Contains("winget install Gyan.FFmpeg", page.Markup);
    }


    [Fact]
    public void A_clip_can_be_played_back_without_leaving_the_page()
    {
        var page = Page();
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav");

        Assert.Empty(page.FindAll("audio"));

        page.FindAll(".mud-icon-button").First(b => b.OuterHtml.Contains("M8 5v14l11-7z")).Click();

        var player = page.FindAll("audio").Single();
        Assert.StartsWith("data:audio/wav;base64,", player.GetAttribute("src"));
        Assert.Contains("wpn_ak_shot_plr.wav", page.Markup);
    }


    // ------------------------------------------------------------------ reopening and export

    /// <summary>Builds a pack, then reloads the page so it is an installed pack like any other.</summary>
    private IRenderedComponent<Build> AfterBuilding(string id = "mypack")
    {
        var page = Page();

        Field(page, "What is your pack called?").Input(id);
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav", "wpn_assault_dist_01.wav");
        BuildPack(page);

        return Page();
    }

    [Fact]
    public void With_nothing_installed_there_is_nothing_to_open()
    {
        Assert.DoesNotContain("Or pick up one you already have", Page().Markup);
    }

    [Fact]
    public void A_pack_just_built_can_be_opened_again_without_restarting_the_server()
    {
        var page = AfterBuilding();

        Assert.Contains("Or pick up one you already have", page.Markup);
        Assert.Contains("mypack", page.Markup);
    }

    [Fact]
    public void Opening_a_pack_brings_back_its_sets_and_its_audio()
    {
        var page = AfterBuilding();

        page.FindAll(".mud-chip").First(c => c.TextContent.Contains("mypack")).Click();

        Assert.Contains("editing", page.Markup);
        Assert.Contains("<b>2</b> clips", page.Markup);
        Assert.Contains("wpn_ak_shot_plr.wav", page.Markup);
        Assert.Contains("body, tail", page.Markup);
    }

    [Fact]
    public void Saving_an_opened_pack_straight_back_does_not_refuse_or_duplicate_it()
    {
        var page = AfterBuilding();

        page.FindAll(".mud-chip").First(c => c.TextContent.Contains("mypack")).Click();
        BuildPack(page);

        Assert.DoesNotContain("Nothing was written", page.Markup);

        var body = Path.Join(_temp.At("plugins", "Resound"), "mypack", "sounds", "assaultrifle", "body");
        Assert.Single(Directory.GetFiles(body));
    }

    [Fact]
    public void Exporting_writes_a_zip_and_says_where_it_went()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav");
        BuildPack(page);

        Assert.DoesNotContain(".zip", page.Markup);

        Click(page, "Export a zip");

        Assert.Contains("mypack.zip", page.Markup);
        Assert.True(File.Exists(Path.Join(_temp.At("plugins", "Resound"), "mypack.zip")));
    }

    [Fact]
    public void There_is_nothing_to_export_until_something_has_been_built()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Assert.DoesNotContain("Export a zip", page.Markup);
    }


    // ------------------------------------------------------------------ guard rails

    [Fact]
    public void The_folder_name_follows_the_pack_name()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("CS:GO Guns");

        Assert.Contains("cs-go-guns", page.Markup);
    }

    [Fact]
    public void Naming_the_folder_by_hand_stops_it_following_the_pack_name()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("CS:GO Guns");
        Click(page, "More options");
        Field(page, "Folder name").Input("csgo");
        Field(page, "What is your pack called?").Input("Something Else");

        Assert.Contains("csgo", page.Markup);
        Assert.DoesNotContain("something-else", page.Markup);
    }

    [Fact]
    public void A_set_that_claims_no_weapons_holds_the_build_back_and_names_itself()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        page.FindAll(".mud-chip").First(c => c.TextContent.Contains("these weapons")).Click();

        Assert.Contains("claims no weapons", page.Markup);
        Assert.Contains("disabled", Button(page, "Build the pack").OuterHtml);
    }

    [Fact]
    public void A_pack_with_no_sound_in_it_is_asked_about_once_and_then_written()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");

        Click(page, "Build the pack");

        // Nothing written yet: it says what would happen and waits.
        Assert.Contains("would still play Tarkov", page.Markup);
        Assert.Contains("Build it anyway", page.Markup);
        Assert.False(File.Exists(Path.Join(_temp.At("plugins", "Resound"), "mypack", PackManifest.FileName)));

        Click(page, "Build it anyway");

        Assert.True(File.Exists(Path.Join(_temp.At("plugins", "Resound"), "mypack", PackManifest.FileName)));
    }

    [Fact]
    public void Adding_the_missing_sound_takes_the_question_away_again()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Click(page, "Build the pack");

        Assert.Contains("Build it anyway", page.Markup);

        Scan(page, "wpn_ak_shot_plr.wav");

        Assert.DoesNotContain("Build it anyway", page.Markup);
        Assert.DoesNotContain("has no sound in it", page.Markup);
    }

    [Fact]
    public void A_pack_with_sound_in_every_set_builds_on_the_first_press()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav");

        Click(page, "Build the pack");

        Assert.DoesNotContain("Build it anyway", page.Markup);
        Assert.Contains("1 clip(s) in body", page.Markup);
    }

    [Fact]
    public void It_says_what_is_about_to_be_written_before_it_is()
    {
        var page = Page();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Scan(page, "wpn_ak_shot_plr.wav", "wpn_assault_dist_01.wav");

        // Two of the five fake weapons are assault rifles, which is the default class.
        Assert.Contains("1 set, 2 weapons, 2 clips", page.Markup);
    }

    [Fact]
    public void A_sets_name_and_folder_are_a_heading_until_you_ask_to_change_them()
    {
        var page = Page();
        Click(page, "Add a sound set");

        Assert.Contains("sounds/assaultrifle", page.Markup);
        Assert.DoesNotContain(page.FindAll("label"), l => l.TextContent.Trim() == "Set name");

        page.FindAll(".mud-icon-button").First(b => !b.OuterHtml.Contains(TrashIcon)).Click();

        Assert.Contains(page.FindAll("label"), l => l.TextContent.Trim() == "Set name");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _temp.Dispose();
        }

        base.Dispose(disposing);
    }
}
