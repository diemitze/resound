using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Resound.Audio
{
    // Both public Init overloads assign playersBridge before the virtual Init runs, so the
    // bridge is the one accessor that resolves the weapon for the local player, bots and
    // Fika peers through a single code path.
    internal static class WeaponLookup
    {
        private static readonly FieldInfo BridgeField =
            AccessTools.Field(typeof(BaseSoundPlayer), "playersBridge");

        // Melee and grenades come through the same sound players, so this is null for them.
        public static Weapon Get(BaseSoundPlayer player)
        {
            return GetItem(player) as Weapon;
        }

        private static Item GetItem(BaseSoundPlayer player)
        {
            if (player == null || BridgeField == null)
                return null;

            BaseSoundPlayer.IObserverToPlayerBridge bridge =
                BridgeField.GetValue(player) as BaseSoundPlayer.IObserverToPlayerBridge;

            return bridge?.Weapon?.Item;
        }

        public static string TemplateId(BaseSoundPlayer player)
        {
            Item item = GetItem(player);
            return item?.TemplateId.ToString();
        }

        public static string Name(BaseSoundPlayer player)
        {
            Item item = GetItem(player);
            return item?.ShortName?.Localized() ?? "unknown";
        }
    }
}
