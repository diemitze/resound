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

        // EFT weapon template id. Turn on "Log weapon template ids" and fire a gun to find it.
        [JsonProperty("templateId")]
        public string TemplateId = "";

        // Optional, ANDed with each other and templateId. One namePattern covers a whole family.
        [JsonProperty("templateIds")]
        public List<string> TemplateIds;

        [JsonProperty("parentId")]
        public string ParentId;

        [JsonProperty("weapClass")]
        public string WeapClass;

        [JsonProperty("caliber")]
        public string Caliber;

        // Regex over the item's internal _name (e.g. "deagle" for every Desert Eagle).
        [JsonProperty("namePattern")]
        public string NamePattern;

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

        // Null follows the unsuppressed value. BO2 peaks every sample alike, so its suppressed rips need turning down.
        [JsonProperty("bodySilencedVolume")]
        public float? BodySilencedVolume;

        [JsonProperty("tailSilencedVolume")]
        public float? TailSilencedVolume;

        // Beats of each shot kept in the full-auto loop. Null fits it to the sample length.
        [JsonProperty("decayBeats")]
        public float? DecayBeats;

        // The shipped sounds are mixed by hand, so this is off unless an entry asks for it.
        [JsonProperty("autoLevel")]
        public bool AutoLevel = false;

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
