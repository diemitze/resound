using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ResoundServer.Services;
using ResoundServer.Web.Pages;

namespace ResoundServer.Tests;

/// <summary>The per-weapon page with two packs covering the AK, the case it exists for.</summary>
public class WeaponsPageTests : BunitContext
{
    private readonly TempPack _temp = new();
    private readonly ResoundService _service;

    public WeaponsPageTests()
    {
        // One pack with two AK sounds, plus a second pack for the cross-pack case.
        InstallTwoSets("bo2ak", "BO2", 0, "weapon_izhmash_ak", "ak74n", "rust_ak");
        Install("rustak", "Rust AK", -101, "weapon_izhmash_ak");
        Install("pistols", "Pistol Pack", 0, "weapon_sig_p226");

        _service = FakeSpt.Service(_temp.At("plugins"));

        Services.AddMudServices();
        Services.AddSingleton(_service);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void Install(string id, string name, int priority, string namePattern)
    {
        var dir = _temp.At("plugins", "Resound", id);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Join(dir, "resound.pack.json"), $$"""
        {
          "id": "{{id}}", "name": "{{name}}", "priority": {{priority}},
          "entries": [
            { "name": "{{name}} rifles", "soundSet": "set", "enabled": true,
              "match": { "namePattern": "{{namePattern}}" } }
          ]
        }
        """);

        _temp.Wav(Path.Join("plugins", "Resound", id, "sounds", "set", "body", "shot.wav"));
    }

    private void InstallTwoSets(string id, string name, int priority, string namePattern,
                                string first, string second)
    {
        var dir = _temp.At("plugins", "Resound", id);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Join(dir, "resound.pack.json"), $$"""
        {
          "id": "{{id}}", "name": "{{name}}", "priority": {{priority}},
          "entries": [
            { "name": "{{first}} rifles", "soundSet": "{{first}}", "enabled": true,
              "match": { "namePattern": "{{namePattern}}" } },
            { "name": "{{second}} rifles", "soundSet": "{{second}}", "enabled": true,
              "match": { "namePattern": "{{namePattern}}" } }
          ]
        }
        """);

        foreach (var set in new[] { first, second })
            _temp.Wav(Path.Join("plugins", "Resound", id, "sounds", set, "body", "shot.wav"));
    }

    private IRenderedComponent<Weapons> Page() => Render<Weapons>();

    [Fact]
    public void Offers_both_sounds_a_single_pack_has_for_the_weapon()
    {
        var page = Page();
        var akRow = page.FindAll("tr").Single(r => r.TextContent.Contains("AK74N"));

        Assert.Contains("ak74n", akRow.TextContent);
        Assert.Contains("rust_ak", akRow.TextContent);
    }

    [Fact]
    public void Picking_the_other_sound_in_the_same_pack_pins_the_set_not_just_the_pack()
    {
        var page = Page();
        var akRow = page.FindAll("tr").Single(r => r.TextContent.Contains("AK74N"));

        akRow.QuerySelectorAll("button.rs-tag").Single(c => c.TextContent.Trim() == "rust_ak").Click();

        var pin = _service.Config.Overrides["59d6088586f774275f37482f"];
        Assert.Equal("bo2ak::rust_ak", pin);

        // And it is what the weapon now plays, not just what the config says.
        var packs = _service.GetPacks();
        var ak = _service.GetWeapons().Single(w => w.InternalName == "weapon_izhmash_ak74n");
        Assert.Equal("rust_ak", _service.Winner(ak, packs)?.Rule.SoundSet);
    }

    [Fact]
    public void A_bare_pack_pin_still_means_that_packs_best_entry()
    {
        var packs = _service.GetPacks();
        var ak = _service.GetWeapons().Single(w => w.InternalName == "weapon_izhmash_ak74n");

        _service.Config.Overrides[ak.Id] = "bo2ak";

        Assert.Equal("ak74n", _service.Winner(ak, packs)?.Rule.SoundSet);
    }

    [Fact]
    public void Offers_every_pack_that_covers_the_weapon()
    {
        var markup = Page().Markup;

        Assert.Contains("ak74n", markup);
        Assert.Contains("rust_ak", markup);
        Assert.Contains("Rust AK", markup);
    }

    [Fact]
    public void Does_not_offer_a_pack_that_has_no_sound_for_anything_shown()
    {
        // The pistol pack covers the P226 only, so the AK and the SMG rows must not offer it.
        var page = Page();
        var akRow = page.FindAll("tr").Single(r => r.TextContent.Contains("AK74N"));

        Assert.DoesNotContain("Pistol Pack", akRow.TextContent);
    }

    [Fact]
    public void Pinning_a_weapon_to_a_pack_changes_what_it_plays()
    {
        var page = Page();
        var akRow = page.FindAll("tr").Single(r => r.TextContent.Contains("AK74N"));

        akRow.QuerySelectorAll("button.rs-tag").Single(c => c.TextContent.Trim() == "Rust AK").Click();

        Assert.Equal("rustak", _service.Config.Overrides["59d6088586f774275f37482f"]);
    }

    [Fact]
    public void Pinning_everything_shown_skips_the_weapons_the_pack_does_not_cover()
    {
        // MudMenu renders its items into the popover provider, not into the page.
        var popover = Render<MudBlazor.MudPopoverProvider>();
        var page = Page();

        page.FindAll("button").First(b => b.TextContent.Contains("Pin all")).Click();
        popover.FindAll(".mud-menu-item").First(i => i.TextContent.Contains("Rust AK")).Click();

        // Only the AK is covered; the four other fake weapons are left alone.
        Assert.Single(_service.Config.Overrides);
        Assert.Equal("rustak", _service.Config.Overrides["59d6088586f774275f37482f"]);
    }
}
