using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EFT.InventoryLogic;
using Newtonsoft.Json;

namespace Resound.Config
{
    // What a weapon has to look like for an entry to claim it. Every field set has to match,
    // and the more fields are set the more specific the entry is when two packs collide.
    internal class WeaponMatch
    {
        [JsonProperty("templateId")]
        public string TemplateId;

        [JsonProperty("templateIds")]
        public List<string> TemplateIds;

        [JsonProperty("parentId")]
        public string ParentId;

        // BSG's own class string: assaultRifle, assaultCarbine, smg, pistol, shotgun,
        // sniperRifle, marksmanRifle, machinegun. Sound sets already partition this way.
        [JsonProperty("weapClass")]
        public string WeapClass;

        [JsonProperty("caliber")]
        public string Caliber;

        // Matched against the item's internal _name, not its localised name.
        [JsonProperty("namePattern")]
        public string NamePattern;

        private Regex _name;
        private bool _nameBad;

        // Weighted so that naming an exact weapon always beats naming a whole class, and
        // narrowing by two criteria beats narrowing by one.
        public int Specificity
        {
            get
            {
                int s = 0;
                if (!string.IsNullOrEmpty(TemplateId)) s += 100;
                if (TemplateIds != null && TemplateIds.Count > 0) s += 90;
                if (!string.IsNullOrEmpty(ParentId)) s += 50;
                if (!string.IsNullOrEmpty(WeapClass)) s += 40;
                if (!string.IsNullOrEmpty(Caliber)) s += 30;
                if (!string.IsNullOrEmpty(NamePattern)) s += 20;
                return s;
            }
        }

        public bool IsEmpty
        {
            get { return Specificity == 0; }
        }

        public bool Matches(WeaponFacts w)
        {
            if (!string.IsNullOrEmpty(TemplateId) &&
                !string.Equals(TemplateId, w.TemplateId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (TemplateIds != null && TemplateIds.Count > 0)
            {
                bool hit = false;
                foreach (string id in TemplateIds)
                {
                    if (string.Equals(id, w.TemplateId, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = true;
                        break;
                    }
                }

                if (!hit)
                    return false;
            }

            if (!string.IsNullOrEmpty(ParentId) &&
                !string.Equals(ParentId, w.ParentId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(WeapClass) &&
                !string.Equals(WeapClass, w.WeapClass, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(Caliber) &&
                !string.Equals(Caliber, w.Caliber, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(NamePattern))
            {
                if (_name == null && !_nameBad)
                {
                    try
                    {
                        _name = new Regex(NamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    }
                    catch (Exception e)
                    {
                        _nameBad = true;
                        Plugin.Log.LogWarning("Bad namePattern '" + NamePattern + "': " + e.Message);
                    }
                }

                if (_nameBad || _name == null || w.Name == null || !_name.IsMatch(w.Name))
                    return false;
            }

            return true;
        }
    }

    // The weapon properties a matcher can test, read once per template.
    internal class WeaponFacts
    {
        public string TemplateId;
        public string ParentId;
        public string WeapClass;
        public string Caliber;
        public string Name;
        public string DisplayName;

        public static WeaponFacts From(Weapon weapon)
        {
            if (weapon == null)
                return null;

            WeaponTemplate t = weapon.Template;

            return new WeaponFacts
            {
                TemplateId = weapon.TemplateId.ToString(),
                ParentId = t?.ParentId?.ToString(),
                WeapClass = t?.weapClass,
                Caliber = weapon.AmmoCaliber,
                Name = t?._name,
                DisplayName = weapon.ShortName?.Localized()
            };
        }
    }

    internal class PackEntry
    {
        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("match")]
        public WeaponMatch Match;

        [JsonProperty("soundSet")]
        public string SoundSet = "";

        [JsonProperty("enabled")]
        public bool Enabled = true;

        [JsonProperty("bodyVolume")]
        public float BodyVolume = 1.0f;

        [JsonProperty("tailVolume")]
        public float TailVolume = 1.0f;

        [JsonProperty("bodySilencedVolume")]
        public float? BodySilencedVolume;

        [JsonProperty("tailSilencedVolume")]
        public float? TailSilencedVolume;

        [JsonProperty("eventVolume")]
        public float EventVolume = 1.0f;

        [JsonProperty("decayBeats")]
        public float? DecayBeats;
    }

    internal class Pack
    {
        // Highest apiVersion this build understands. A pack declaring more is skipped whole
        // rather than half-loaded, because the fields it needs would silently read as defaults.
        public const int SupportedApiVersion = 1;

        [JsonProperty("id")]
        public string Id = "";

        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("author")]
        public string Author = "";

        [JsonProperty("apiVersion")]
        public int ApiVersion = 1;

        [JsonProperty("priority")]
        public int Priority = 0;

        [JsonProperty("entries")]
        public List<PackEntry> Entries = new List<PackEntry>();

        // Absolute path to the folder holding the manifest; sound sets resolve under sounds/.
        [JsonIgnore]
        public string Directory = "";
    }

    // An entry resolved onto a specific weapon, with the pack it came from already applied.
    internal class SoundProfile
    {
        public string Label;      // "pack:entry", for logs
        public string SetDir;     // absolute, so two packs can both ship a set called m4a1
        public float BodyVolume;
        public float TailVolume;
        public float? BodySilencedVolume;
        public float? TailSilencedVolume;
        public float EventVolume;
        public float? DecayBeats;
    }
}
