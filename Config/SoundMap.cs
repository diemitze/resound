using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Resound.Config
{
    internal class WeaponMapping
    {
        [JsonProperty("name")]
        public string Name = "";

        // EFT weapon template id. Turn on logWeaponIds and fire a gun to find it.
        [JsonProperty("templateId")]
        public string TemplateId = "";

        // Folder under sounds/ holding this weapon's replacement clips.
        [JsonProperty("soundSet")]
        public string SoundSet = "";

        [JsonProperty("enabled")]
        public bool Enabled = true;

        // Multipliers on the vanilla bank's volume, 1.0 being stock for that weapon.
        [JsonProperty("bodyVolume")]
        public float BodyVolume = 1.0f;

        [JsonProperty("tailVolume")]
        public float TailVolume = 1.0f;

        // Suppressed banks, null meaning "same as unsuppressed". Worth setting when the source
        // game normalises every sample to the same peak (BO2 does), because EFT's silenced bank
        // volume is tuned for BSG's own much quieter suppressed recording.
        [JsonProperty("bodySilencedVolume")]
        public float? BodySilencedVolume;

        [JsonProperty("tailSilencedVolume")]
        public float? TailSilencedVolume;

        // How many beats of each shot survive in the full-auto loop, null meaning the default 4.
        // A sample longer than this window gets cut short with a linear fade. Raising it keeps
        // more of the sample but overlaps more copies, which past a point is mush not fidelity.
        [JsonProperty("decayBeats")]
        public float? DecayBeats;

        // Scales BSG's per-event volume, which is not always 1.0 (the MP-155 ships 1.1 and 1.2).
        [JsonProperty("eventVolume")]
        public float EventVolume = 1.0f;
    }

    internal class SoundMap
    {
        [JsonProperty("weapons")]
        public List<WeaponMapping> Weapons = new List<WeaponMapping>();

        public static SoundMap Load(string path)
        {
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("No mapping.json at " + path + "; no sounds will be replaced.");
                return new SoundMap();
            }

            try
            {
                return JsonConvert.DeserializeObject<SoundMap>(File.ReadAllText(path)) ?? new SoundMap();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("mapping.json is invalid, running with no replacements: " + e.Message);
                return new SoundMap();
            }
        }
    }
}
