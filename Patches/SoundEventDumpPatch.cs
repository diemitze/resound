using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EFT;
using HarmonyLib;
using Resound.Audio;
using SPT.Reflection.Patching;

namespace Resound.Patches
{
    // Event keys live on the prefabs, not in Assembly-CSharp, so a live player is the only place to read them.
    internal class SoundEventDumpPatch : ModulePatch
    {
        // The override calls base.Init first, so playersBridge is already set here.
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WeaponSoundPlayer), nameof(WeaponSoundPlayer.Init),
                new[] { typeof(BifacialTransform) });
        }

        private static readonly HashSet<string> Dumped =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [PatchPostfix]
        private static void Postfix(WeaponSoundPlayer __instance)
        {
            if (!Plugin.LogSoundEvents.Value || __instance == null)
                return;

            string templateId = WeaponLookup.TemplateId(__instance) ?? "unknown";
            if (!Dumped.Add(templateId))
                return;

            StringBuilder sb = new StringBuilder();
            sb.Append("sound events for ").Append(WeaponLookup.Name(__instance))
              .Append(" (").Append(templateId).Append(')');

            Append(sb, "MainSounds", __instance.MainSounds);
            Append(sb, "AdditionalSounds", __instance.AdditionalSounds);

            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void Append(StringBuilder sb, string label,
            IEnumerable<BaseSoundPlayer.SoundElement> elements)
        {
            sb.Append("\n  ").Append(label).Append(':');

            int n = 0;
            if (elements != null)
            {
                foreach (BaseSoundPlayer.SoundElement e in elements)
                {
                    if (e == null)
                        continue;

                    n++;
                    sb.Append("\n    ").Append(e.EventName)
                      .Append("  clips=").Append(e.SoundClips == null ? 0 : e.SoundClips.Length)
                      .Append(" vol=").Append(e.Volume)
                      .Append(" rolloff=").Append(e.RollOff);
                }
            }

            if (n == 0)
                sb.Append(" none");
        }
    }
}
