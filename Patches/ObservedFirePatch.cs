using System.Reflection;
using Resound.Audio;
using Resound.Config;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using EFT.NextObservedPlayer;

namespace Resound.Patches
{
    // Bots, Scavs and Fika peers all fire through here.
    internal class ObservedFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ObservedPlayerHandsController), nameof(ObservedPlayerHandsController.FireShotSound));
        }

        [PatchPrefix]
        private static void Prefix(ObservedPlayerHandsController __instance, BaseSoundPlayer baseSoundPlayer)
        {
            if (!Plugin.Enabled.Value)
                return;

            // Melee and grenades come through here too, on other BaseSoundPlayer types.
            if (!(baseSoundPlayer is WeaponSoundPlayer weaponSoundPlayer))
                return;

            Weapon weapon = __instance?.Weapon;
            if (weapon == null)
                return;

            BankSwapper.Apply(weaponSoundPlayer, weapon.TemplateId.ToString(), PackRegistry.Resolve(weapon));
        }
    }
}
