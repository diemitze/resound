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
    }

    internal class SoundMap
    {
        [JsonProperty("logWeaponIds")]
        public bool LogWeaponIds = false;

        [JsonProperty("weapons")]
        public List<WeaponMapping> Weapons = new List<WeaponMapping>();

        private readonly Dictionary<string, WeaponMapping> _byTemplate =
            new Dictionary<string, WeaponMapping>(StringComparer.OrdinalIgnoreCase);

        public static SoundMap Load(string path)
        {
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("No mapping.json at " + path + "; no sounds will be replaced.");
                return new SoundMap();
            }

            try
            {
                SoundMap map = JsonConvert.DeserializeObject<SoundMap>(File.ReadAllText(path)) ?? new SoundMap();
                map.Index();
                return map;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("mapping.json is invalid, running with no replacements: " + e.Message);
                return new SoundMap();
            }
        }

        private void Index()
        {
            foreach (WeaponMapping w in Weapons)
            {
                if (!w.Enabled || string.IsNullOrEmpty(w.TemplateId) || string.IsNullOrEmpty(w.SoundSet))
                    continue;

                _byTemplate[w.TemplateId] = w;
            }
        }

        public WeaponMapping Find(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return null;

            _byTemplate.TryGetValue(templateId, out WeaponMapping w);
            return w;
        }
    }
}

