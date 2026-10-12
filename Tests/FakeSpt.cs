using System.Runtime.CompilerServices;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>A ResoundService over a few made-up weapons. SPT tables are created uninitialized with only the read fields set.</summary>
public static class FakeSpt
{
    public record Weapon(string Id, string Name, string WeapClass, string Caliber);

    public static readonly Weapon[] DefaultWeapons =
    [
        new("5926bb2186f7744b1c6c6e60", "weapon_hk_mp5", "smg", "Caliber9x19PARA"),
        new("5ba26383d4351e00334c93d9", "weapon_hk_mp7", "smg", "Caliber46x30"),
        new("59d6088586f774275f37482f", "weapon_izhmash_ak74n", "assaultRifle", "Caliber545x39"),
        new("5b0bbe4e5acfc40dc528a72d", "weapon_ds_arms_sa58", "assaultRifle", "Caliber762x51"),
        new("56d59856d2720bd8418b456a", "weapon_sig_p226", "pistol", "Caliber9x19PARA")
    ];

    public static ResoundService Service(params Weapon[] weapons) => Service(null, weapons);

    /// <summary>pluginsRoot stands in for BepInEx/plugins.</summary>
    public static ResoundService Service(string? pluginsRoot, params Weapon[] weapons)
    {
        if (weapons.Length == 0)
            weapons = DefaultWeapons;

        var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        var locales = (LocaleTable)RuntimeHelpers.GetUninitializedObject(typeof(LocaleTable));

        var items = new Dictionary<MongoId, TemplateItem>();

        foreach (var w in weapons)
            items[new MongoId(w.Id)] = Item(w);

        Set(templates, "Items", items);

        // Empty on purpose, to exercise the fallback to internal names.
        Set(locales, "Global", new Dictionary<string, object>());

        return new ResoundService(templates, locales,
            new ModHelper(new FileUtil(), new JsonUtil([])), new TestLogger<ResoundService>())
        {
            PluginsRootOverride = pluginsRoot
        };
    }

    private static TemplateItem Item(Weapon w)
    {
        var item = (TemplateItem)RuntimeHelpers.GetUninitializedObject(typeof(TemplateItem));

        Set(item, "Type", "Item");
        Set(item, "Name", w.Name);
        Set(item, "Parent", new MongoId("5447b5f14bdc2d61278b4567"));

        var propsType = typeof(TemplateItem).GetProperty("Properties")!.PropertyType;
        var props = RuntimeHelpers.GetUninitializedObject(propsType);

        Set(props, "WeapClass", w.WeapClass);
        Set(props, "AmmoCaliber", w.Caliber);
        Set(props, "ShortName", ShortName(w.Name));

        Set(item, "Properties", props);

        return item;
    }

    /// <summary>weapon_hk_mp5 reads as MP5, which is what a name-less locale table leaves behind.</summary>
    private static string ShortName(string internalName)
    {
        var parts = internalName.Split('_');
        return parts.Length > 0 ? parts[^1].ToUpperInvariant() : internalName;
    }

    private static void Set(object target, string property, object? value)
    {
        var p = target.GetType().GetProperty(property)
                ?? throw new InvalidOperationException(
                    $"{target.GetType().Name} has no {property}; SPT's model changed and this fake needs updating.");

        if (value is Dictionary<string, object> && p.PropertyType != value.GetType())
            value = Activator.CreateInstance(p.PropertyType);

        p.SetValue(target, value);
    }
}
