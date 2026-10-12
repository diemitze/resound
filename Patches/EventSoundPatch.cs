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
    // OnSound, EventReceiver and OnUseProp all end in PlayRandomClip. Substituting here never touches the pooled prefab.
    internal class EventSoundPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseSoundPlayer), nameof(BaseSoundPlayer.PlayRandomClip));
        }

        private static readonly HashSet<string> Logged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Logged here, not in its own patch: a prefix returning false would silence it.
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

            // Scale BSG's per-event Volume rather than replace it.
            __instance.PlayClip(clip, soundElement.RollOff, soundElement.Volume * profile.EventVolume);

            Plugin.Dbg("event swap: " + soundElement.EventName + " -> " + profile.Label +
                       " (" + clip.name + ", " + clips.Length + " variant(s))");

            return false;
        }
    }
}
