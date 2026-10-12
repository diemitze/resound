using ResoundServer.Models;

namespace ResoundServer.Tests;

/// <summary>What a selection becomes. The claims preview must be right before anything is written.</summary>
public class SetEditorTests
{
    private static readonly List<WeaponRow> Weapons =
    [
        new() { Id = "1", Name = "HK MP5", ShortName = "MP5", WeapClass = "smg", Caliber = "Caliber9x19PARA", InternalName = "weapon_hk_mp5" },
        new() { Id = "2", Name = "HK MP7", ShortName = "MP7", WeapClass = "smg", Caliber = "Caliber46x30", InternalName = "weapon_hk_mp7" },
        new() { Id = "3", Name = "PP-19-01", ShortName = "Vityaz", WeapClass = "smg", Caliber = "Caliber9x19PARA", InternalName = "weapon_izhmash_pp1901" },
        new() { Id = "4", Name = "AK-74N", ShortName = "AK-74N", WeapClass = "assaultRifle", Caliber = "Caliber545x39", InternalName = "weapon_izhmash_ak74n" },
        new() { Id = "5", Name = "SA-58", ShortName = "SA-58", WeapClass = "assaultRifle", Caliber = "Caliber762x51", InternalName = "weapon_ds_arms_sa58" }
    ];

    private static SetEditor Editor(string weapClass = "smg") => new(Weapons, weapClass);

    // ------------------------------------------------------------------ claims

    [Fact]
    public void A_class_claims_every_weapon_in_it()
    {
        var editor = Editor();

        Assert.Equal(3, editor.Claims().Count);
    }

    [Fact]
    public void A_calibre_narrows_the_class_rather_than_replacing_it()
    {
        var editor = Editor();
        editor.SetCaliber("Caliber9x19PARA");

        var claims = editor.Claims();

        Assert.Equal(2, claims.Count);
        Assert.DoesNotContain(claims, w => w.ShortName == "MP7");
    }

    [Fact]
    public void Switching_class_drops_a_calibre_that_no_longer_applies()
    {
        var editor = Editor();
        editor.SetCaliber("Caliber9x19PARA");
        editor.SetClass("assaultRifle");

        Assert.Null(editor.Draft.Rule.Caliber);
        Assert.Equal(2, editor.Claims().Count);
    }

    [Fact]
    public void One_picked_weapon_becomes_a_template_id()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.Add("1");
        editor.WeaponsChanged();

        Assert.Equal("1", editor.Draft.Rule.TemplateId);
        Assert.Null(editor.Draft.Rule.TemplateIds);
        Assert.Single(editor.Claims());
    }

    [Fact]
    public void Several_picked_weapons_become_a_template_id_list()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.Add("1");
        editor.Weapons.Add("4");
        editor.WeaponsChanged();

        Assert.Null(editor.Draft.Rule.TemplateId);
        Assert.Equal(2, editor.Draft.Rule.TemplateIds!.Count);
        Assert.Equal(2, editor.Claims().Count);
    }

    [Fact]
    public void Picking_weapons_clears_the_class_because_the_two_would_be_anded()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.Add("4"); // an assault rifle, while the class was smg
        editor.WeaponsChanged();

        Assert.Null(editor.Draft.Rule.WeapClass);
        Assert.Single(editor.Claims());
    }

    [Fact]
    public void Switching_back_to_a_class_drops_the_picked_weapons()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.Add("1");
        editor.WeaponsChanged();

        editor.SetMode(MatchMode.Class);

        Assert.Null(editor.Draft.Rule.TemplateId);
        Assert.Null(editor.Draft.Rule.TemplateIds);
        Assert.Equal(3, editor.Claims().Count);
    }

    [Fact]
    public void An_empty_weapon_selection_claims_nothing()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);

        Assert.Empty(editor.Claims());
        Assert.Equal(0, editor.Draft.Rule.Specificity);
    }

    // ------------------------------------------------------------------ naming

    [Fact]
    public void The_folder_follows_the_class()
    {
        var editor = Editor();

        Assert.Equal("smg", editor.Draft.SoundSet);

        editor.SetClass("assaultRifle");
        Assert.Equal("assaultrifle", editor.Draft.SoundSet);
    }

    [Fact]
    public void The_folder_picks_up_the_calibre()
    {
        var editor = Editor();
        editor.SetCaliber("Caliber9x19PARA");

        Assert.Equal("smg-9x19para", editor.Draft.SoundSet);
    }

    [Fact]
    public void The_folder_follows_the_picked_weapon()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.Add("1");
        editor.WeaponsChanged();

        Assert.Equal("mp5", editor.Draft.SoundSet);
    }

    [Fact]
    public void Naming_the_folder_stops_it_following_anything()
    {
        var editor = Editor();
        editor.NameFolder("my_smg");

        editor.SetClass("assaultRifle");
        editor.SetCaliber("Caliber762x51");

        Assert.Equal("my_smg", editor.Draft.SoundSet);
    }

    [Fact]
    public void The_label_follows_the_selection_until_it_is_typed_over()
    {
        var editor = Editor();
        Assert.Equal("Every smg", editor.Draft.Rule.Name);

        editor.SetClass("assaultRifle");
        Assert.Equal("Every assault rifle", editor.Draft.Rule.Name);

        editor.Draft.Rule.Name = "Battle rifles";
        editor.SetCaliber("Caliber762x51");

        Assert.Equal("Battle rifles", editor.Draft.Rule.Name);
    }

    [Fact]
    public void Each_set_names_itself_independently_of_the_others()
    {
        var a = Editor();
        var b = Editor();

        a.Draft.Rule.Name = "Mine";
        b.SetClass("assaultRifle");

        // b's label must still follow its own selection: it never had a name typed into it.
        Assert.Equal("Mine", a.Draft.Rule.Name);
        Assert.Equal("Every assault rifle", b.Draft.Rule.Name);
    }

    [Fact]
    public void A_set_of_several_weapons_describes_itself_by_the_first_and_a_count()
    {
        var editor = Editor();
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.Add("1");
        editor.Weapons.Add("2");
        editor.Weapons.Add("3");
        editor.WeaponsChanged();

        Assert.Equal("MP5 and 2 more", editor.Draft.Rule.Name);
        Assert.Equal("mp5-and-2-more", editor.Draft.SoundSet);
    }

    [Fact]
    public void A_family_picked_together_names_its_folder_after_the_family()
    {
        List<WeaponRow> aks =
        [
            new() { Id = "a", Name = "AK-74N", ShortName = "AK-74N", WeapClass = "assaultRifle" },
            new() { Id = "b", Name = "AKM", ShortName = "AKM", WeapClass = "assaultRifle" },
            new() { Id = "c", Name = "AKS-74U", ShortName = "AKS-74U", WeapClass = "assaultRifle" }
        ];
        var editor = new SetEditor(aks, "assaultRifle");
        editor.SetMode(MatchMode.Weapons);
        editor.Weapons.UnionWith(["a", "b", "c"]);
        editor.WeaponsChanged();

        Assert.Equal("ak", editor.Draft.SoundSet);
    }

    // ------------------------------------------------------------------ calibres

    [Fact]
    public void Only_the_calibres_a_class_actually_uses_are_offered()
    {
        var editor = Editor();

        Assert.Equal(["Caliber46x30", "Caliber9x19PARA"], editor.Calibres("smg"));
        Assert.Equal(["Caliber545x39", "Caliber762x51"], editor.Calibres("assaultRifle"));
    }
}
