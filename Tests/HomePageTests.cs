using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ResoundServer.Services;
using ResoundServer.Web.Pages;

namespace ResoundServer.Tests;

/// <summary>The overview page and its health banner, against packs in a temp plugins folder.</summary>
public class HomePageTests : BunitContext
{
    private readonly TempPack _temp = new();

    public HomePageTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(FakeSpt.Service(_temp.At("plugins")));
        Services.AddSingleton(new PackValidator());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>Drops a pack into the fake plugins folder, manifest and all.</summary>
    private void Install(string id, string manifest, params string[] audio)
    {
        var dir = _temp.At("plugins", "Resound", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, "resound.pack.json"), manifest);

        foreach (var file in audio)
            _temp.Wav(Path.Join("plugins", "Resound", id, "sounds", file));
    }

    [Fact]
    public void Renders_the_counts_and_the_pack_chooser()
    {
        var page = Render<Home>();

        Assert.Contains("Sound packs", page.Markup);
        Assert.Contains("Weapons with new sounds", page.Markup);
        Assert.Contains("Which pack do you want to hear?", page.Markup);
        Assert.Contains("Auto", page.Markup);
    }

    [Fact]
    public void Says_how_many_weapons_stay_vanilla()
    {
        var page = Render<Home>();

        // Five weapons in the fake, none of them covered by a pack.
        Assert.Contains("of 5 in the game", page.Markup);
    }

    [Fact]
    public void Points_at_the_pack_format_when_nothing_is_installed()
    {
        var page = Render<Home>();

        Assert.Contains("No add-on packs installed", page.Markup);
    }

    [Fact]
    public void Shows_no_health_banner_when_no_packs_are_installed()
    {
        Assert.DoesNotContain("problem", Render<Home>().Markup);
    }

    [Fact]
    public void A_healthy_pack_raises_no_banner()
    {
        Install("good", """
            { "id": "good", "name": "Good Pack", "entries": [
              { "name": "SMGs", "match": { "weapClass": "smg" }, "soundSet": "myset" } ] }
            """, "myset/body/shot.wav");

        var page = Render<Home>();

        Assert.Contains("Good Pack", page.Markup);
        Assert.DoesNotContain("problem", page.Markup);
    }

    [Fact]
    public void A_pack_whose_set_has_no_audio_raises_the_banner()
    {
        Install("broken", """
            { "id": "broken", "name": "Broken Pack", "entries": [
              { "name": "SMGs", "match": { "weapClass": "smg" }, "soundSet": "missing" } ] }
            """, "myset/body/shot.wav");

        var page = Render<Home>();

        Assert.Contains("problem", page.Markup);
        Assert.Contains("Each one is a weapon still playing its Tarkov sound", page.Markup);
    }

    [Fact]
    public void The_banner_is_collapsed_until_it_is_asked_to_open()
    {
        Install("broken", """
            { "id": "broken", "name": "Broken Pack", "entries": [
              { "name": "SMGs", "match": { "weapClass": "smg" }, "soundSet": "missing" } ] }
            """, "myset/body/shot.wav");

        var page = Render<Home>();

        Assert.DoesNotContain("no folder called", page.Markup);

        page.FindAll("button").First(b => b.TextContent.Contains("Show me")).Click();

        Assert.Contains("no folder called", page.Markup);
        Assert.Contains("Broken Pack", page.Markup);
    }

    [Fact]
    public void A_file_the_plugin_cannot_load_is_named_in_the_banner_with_its_fix()
    {
        Install("broken", """
            { "id": "broken", "name": "Broken Pack", "entries": [
              { "name": "SMGs", "match": { "weapClass": "smg" }, "soundSet": "myset" } ] }
            """);

        _temp.File_("plugins/Resound/broken/sounds/myset/body/shot.mp3");

        var page = Render<Home>();
        page.FindAll("button").First(b => b.TextContent.Contains("Show me")).Click();

        Assert.Contains("shot.mp3", page.Markup);
        Assert.Contains("ffmpeg -i", page.Markup);
    }

    [Fact]
    public void An_installed_pack_is_counted_against_the_weapons_it_covers()
    {
        Install("good", """
            { "id": "good", "name": "Good Pack", "entries": [
              { "name": "SMGs", "match": { "weapClass": "smg" }, "soundSet": "myset" } ] }
            """, "myset/body/shot.wav");

        // Two of the five fake weapons are SMGs, so three are left on their Tarkov sound.
        Assert.Contains("the other 3 stay vanilla", Render<Home>().Markup);
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
