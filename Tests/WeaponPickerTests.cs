using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ResoundServer.Models;
using ResoundServer.Web.Shared;

namespace ResoundServer.Tests;

/// <summary>Renders the picker to catch binding and null mistakes a compile does not.</summary>
public class WeaponPickerTests : BunitContext
{
    private static readonly List<WeaponRow> Weapons =
    [
        new() { Id = "1", Name = "HK MP5", ShortName = "MP5", WeapClass = "smg", Caliber = "Caliber9x19PARA" },
        new() { Id = "2", Name = "HK MP7", ShortName = "MP7", WeapClass = "smg", Caliber = "Caliber46x30" },
        new() { Id = "3", Name = "AK-74N", ShortName = "AK-74N", WeapClass = "assaultRifle", Caliber = "Caliber545x39" }
    ];

    public WeaponPickerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<WeaponPicker> Render(HashSet<string> selected, Action? onChanged = null)
    {
        return Render<WeaponPicker>(p => p
            .Add(c => c.Weapons, Weapons)
            .Add(c => c.Selected, selected)
            .Add(c => c.OnChanged, () => onChanged?.Invoke()));
    }

    [Fact]
    public void Renders_every_weapon_and_a_chip_per_class()
    {
        var page = Render([]);

        Assert.Contains("MP5", page.Markup);
        Assert.Contains("AK-74N", page.Markup);
        Assert.Contains("Assault rifle", page.Markup);
        Assert.Contains("0 selected of 3", page.Markup);
    }

    [Fact]
    public void Shows_the_calibre_the_way_people_say_it()
    {
        var page = Render([]);

        Assert.Contains("9x19", page.Markup);
        Assert.DoesNotContain("9x19PARA", page.Markup);
        Assert.DoesNotContain("Caliber9x19PARA", page.Markup);
    }

    [Fact]
    public void Adding_everything_shown_selects_it_and_reports_the_change()
    {
        var selected = new HashSet<string>();
        var changed = 0;

        var page = Render(selected, () => changed++);
        page.FindAll("button").First(b => b.TextContent.Contains("Add these")).Click();

        Assert.Equal(3, selected.Count);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Clearing_empties_the_selection()
    {
        var selected = new HashSet<string> { "1", "2" };

        var page = Render(selected);
        page.FindAll("button").First(b => b.TextContent.Contains("Clear")).Click();

        Assert.Empty(selected);
    }

    [Fact]
    public void An_existing_selection_is_shown_as_selected()
    {
        var page = Render(["1", "2"]);

        Assert.Contains("2 selected of 3", page.Markup);
    }

    [Fact]
    public void Searching_narrows_the_list()
    {
        var page = Render([]);

        page.Find("input").Input("MP7");

        Assert.Contains("MP7", page.Markup);
        Assert.DoesNotContain("AK-74N", page.Markup);
        Assert.Contains("Add these 1", page.Markup);
    }

    [Fact]
    public void A_search_that_matches_nothing_says_so_rather_than_rendering_an_empty_box()
    {
        var page = Render([]);

        page.Find("input").Input("nothing like this exists");

        Assert.Contains("Nothing matches that", page.Markup);
    }

    [Fact]
    public void An_empty_weapon_list_does_not_throw()
    {
        var page = Render<WeaponPicker>(p => p
            .Add(c => c.Weapons, [])
            .Add(c => c.Selected, []));

        Assert.Contains("0 selected of 0", page.Markup);
    }
}
