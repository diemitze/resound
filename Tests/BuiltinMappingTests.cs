using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>mapping.json keeps match fields on the entry, not under "match"; family entries must still claim weapons.</summary>
public class BuiltinMappingTests
{
    private static string Install(TempPack temp, string mappingJson)
    {
        var plugins = temp.At("plugins");
        var path = Path.Join(plugins, "Resound", "sounds", "mapping.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, mappingJson);
        return plugins;
    }

    [Fact]
    public void NamePatternEntryClaimsItsWeapons()
    {
        using var temp = new TempPack();

        var plugins = Install(temp, """
        {
          "weapons": [
            { "name": "AK family", "namePattern": "izhmash_ak", "soundSet": "ak74n", "enabled": true }
          ]
        }
        """);

        var service = FakeSpt.Service(plugins);
        var packs = service.GetPacks();
        var ak = service.GetWeapons().Single(w => w.InternalName == "weapon_izhmash_ak74n");

        var winner = service.Winner(ak, packs);

        Assert.NotNull(winner);
        Assert.Equal(ResoundService.BuiltinId, winner.Value.Pack.Id);
        Assert.Equal("ak74n", winner.Value.Rule.SoundSet);
    }

    [Fact]
    public void WeapClassAndCaliberEntryClaimsItsWeapons()
    {
        using var temp = new TempPack();

        var plugins = Install(temp, """
        {
          "weapons": [
            { "name": "7.62x51 rifles", "weapClass": "assaultRifle", "caliber": "Caliber762x51",
              "soundSet": "scarh", "enabled": true }
          ]
        }
        """);

        var service = FakeSpt.Service(plugins);
        var packs = service.GetPacks();

        var sa58 = service.GetWeapons().Single(w => w.InternalName == "weapon_ds_arms_sa58");
        var ak = service.GetWeapons().Single(w => w.InternalName == "weapon_izhmash_ak74n");

        Assert.Equal("scarh", service.Winner(sa58, packs)?.Rule.SoundSet);
        Assert.Null(service.Winner(ak, packs));
    }

    [Fact]
    public void TemplateIdsListEntryClaimsEveryIdInIt()
    {
        using var temp = new TempPack();

        var plugins = Install(temp, """
        {
          "weapons": [
            { "name": "Two SMGs", "soundSet": "mp5", "enabled": true,
              "templateIds": ["5926bb2186f7744b1c6c6e60", "5ba26383d4351e00334c93d9"] }
          ]
        }
        """);

        var service = FakeSpt.Service(plugins);
        var packs = service.GetPacks();
        var weapons = service.GetWeapons();

        foreach (var name in new[] { "weapon_hk_mp5", "weapon_hk_mp7" })
            Assert.Equal("mp5", service.Winner(weapons.Single(w => w.InternalName == name), packs)?.Rule.SoundSet);
    }
}
