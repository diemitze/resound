using System.Reflection;
using Resound.Audio;
using Resound.Config;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Resound.Patches
{
    // The one place the sound player and the Weapon are both in scope.
    // Prefix, so banks are swapped before FireBullet reads them.
    internal class LocalFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player.FirearmController), "method_59");
        }

        [PatchPrefix]
        private static void Prefix(Player.FirearmController __instance, WeaponSoundPlayer weaponSoundPlayer)
        {
            if (!Plugin.Enabled.Value || weaponSoundPlayer == null)
                return;

            Weapon weapon = __instance?.Weapon;
            if (weapon == null)
                return;

            string templateId = weapon.TemplateId.ToString();

            if (Plugin.LogWeaponIds.Value)
                Plugin.Log.LogInfo("fired: " + weapon.ShortName?.Localized() + "  templateId=" + templateId);

            BankSwapper.Apply(weaponSoundPlayer, templateId, PackRegistry.Resolve(weapon));
        }
    }
}
