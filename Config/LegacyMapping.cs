using System.Collections.Generic;
using System.IO;

namespace Resound.Config
{
    // The pre-pack sounds/mapping.json, adapted into a pack so there is one code path.
    internal static class LegacyMapping
    {
        public const string PackId = "builtin";

        // The shipped sounds are the floor, not the ceiling. Anything the user chose to install
        // outranks them, so adding a weapon here later can never take it back off someone's pack.
        // A pack that wants to sit below the built-ins can still say so with a lower priority.
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
                Entries = new List<PackEntry>()
            };

            foreach (WeaponMapping w in map.Weapons)
            {
                pack.Entries.Add(new PackEntry
                {
                    Name = w.Name,
                    SoundSet = w.SoundSet,
                    Enabled = w.Enabled,
                    Match = new WeaponMatch { TemplateId = w.TemplateId },
                    BodyVolume = w.BodyVolume,
                    TailVolume = w.TailVolume,
                    BodySilencedVolume = w.BodySilencedVolume,
                    TailSilencedVolume = w.TailSilencedVolume,
                    EventVolume = w.EventVolume,
                    DecayBeats = w.DecayBeats
                });
            }

            return pack;
        }
    }
}
