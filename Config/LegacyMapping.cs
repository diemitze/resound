using System.Collections.Generic;
using System.IO;

namespace Resound.Config
{
    // The pre-pack sounds/mapping.json, adapted into a pack so there is one code path.
    internal static class LegacyMapping
    {
        public const string PackId = "builtin";

        // The floor: anything installed outranks the shipped sounds, so a weapon added here later never steals one back.
        public const int Priority = -100;

        public static Pack Load(string pluginDir)
        {
            string root = Path.Combine(pluginDir, "Resound");
            string path = Path.Combine(Path.Combine(root, "sounds"), "mapping.json");

            if (!File.Exists(path))
                return null;

            SoundMap map = SoundMap.Load(path);
            if (map.Weapons.Count == 0)
                return null;

            Pack pack = new Pack
            {
                Id = PackId,
                Name = "Resound built-in",
                Directory = root,
                Priority = Priority,
                AutoLevel = false,
                Entries = new List<PackEntry>()
            };

            foreach (WeaponMapping w in map.Weapons)
            {
                pack.Entries.Add(new PackEntry
                {
                    Name = w.Name,
                    SoundSet = w.SoundSet,
                    Enabled = w.Enabled,
                    Match = new WeaponMatch
                    {
                        // An empty string in the json reads as "unset" for matching purposes.
                        TemplateId = string.IsNullOrEmpty(w.TemplateId) ? null : w.TemplateId,
                        TemplateIds = w.TemplateIds,
                        ParentId = w.ParentId,
                        WeapClass = w.WeapClass,
                        Caliber = w.Caliber,
                        NamePattern = w.NamePattern
                    },
                    BodyVolume = w.BodyVolume,
                    TailVolume = w.TailVolume,
                    BodySilencedVolume = w.BodySilencedVolume,
                    TailSilencedVolume = w.TailSilencedVolume,
                    EventVolume = w.EventVolume,
                    DecayBeats = w.DecayBeats,
                    AutoLevel = w.AutoLevel
                });
            }

            return pack;
        }
    }
}
