using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using Resound.Audio;
using Resound.Config;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Resound.Patches
{
    // Reload and foley audio reaches the player as animator events:
    //   OnSound(name) -> SoundEventHandler(name) -> searches AdditionalSounds -> PlayRandomClip
    // EventReceiver and OnUseProp are two more entry points into the same funnel, so patching
    // PlayRandomClip covers all three. Weapons keep everything in AdditionalSounds; MainSounds
    // is empty on every weapon checked so far.
    //
    // Substituting here never writes to the prefab, which matters because prefabs are pooled;
    // that is the whole reason BankSwapper needs its save/restore dance on the fire path.
    internal class EventSoundPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseSoundPlayer), nameof(BaseSoundPlayer.PlayRandomClip));
        }

        private static readonly HashSet<string> Logged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Logging lives here rather than in its own patch: a prefix returning false suppresses
        // the remaining prefixes, so a separate logger would go quiet exactly when a swap fires.
        [PatchPrefix]
        private static bool Prefix(BaseSoundPlayer __instance, BaseSoundPlayer.SoundElement soundElement)
        {
            if (soundElement == null || string.IsNullOrEmpty(soundElement.EventName))
                return true;

            // Props, grenades and melee share this funnel and have no weapon behind them.
            Weapon weapon = WeaponLookup.Get(__instance);
            if (weapon == null)
                return true;

            string templateId = weapon.TemplateId.ToString();

            if (Plugin.LogSoundEvents.Value && Logged.Add(templateId + '/' + soundElement.EventName))
                Plugin.Log.LogInfo("event fired: " + soundElement.EventName +
                                   "  (" + WeaponLookup.Name(__instance) + ' ' + templateId + ')');

            if (!Plugin.Enabled.Value)
                return true;

            SoundProfile profile = PackRegistry.Resolve(weapon);
            if (profile == null)
                return true;

            SoundSet set = ClipLibrary.Get(profile.SetDir);
            AudioClip[] clips = set?.Event(soundElement.EventName);
            if (clips == null || clips.Length == 0)
                return true;

            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null || clip.length <= 0f)
                return true;

            // BSG's own Volume and RollOff carry per-event mixing (the MP-155 ranges 1.0 to 1.2),
            // so scale them rather than replacing them, same principle as cloning the fire bank.
            __instance.PlayClip(clip, soundElement.RollOff, soundElement.Volume * profile.EventVolume);

            Plugin.Dbg("event swap: " + soundElement.EventName + " -> " + profile.Label +
                       " (" + clip.name + ", " + clips.Length + " variant(s))");

            return false;
        }
    }
}
