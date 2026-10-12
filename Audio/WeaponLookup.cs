using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Resound.Audio
{
    // playersBridge is set before the virtual Init, so it resolves the weapon for players, bots and Fika peers alike.
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
