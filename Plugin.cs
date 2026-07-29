using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Resound.Audio;
using Resound.Config;
using Resound.Patches;

namespace Resound
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.20fpsguy.resound";
        public const string NAME = "Resound";
        public const string VERSION = "1.1.0";

        public static ManualLogSource Log;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> Debug;
        public static ConfigEntry<bool> LogSoundEvents;
        public static ConfigEntry<bool> LogWeaponIds;
        public static ConfigEntry<string> ActivePack;

        // BindPackSelector runs after discovery, by which point Awake has finished with `this`.
        private static Plugin Instance;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            Enabled = Config.Bind(
                "1. General",
                "Enable sound replacement",
                true,
                "Swap mapped weapons over to their replacement sounds. Turn off to hear vanilla " +
                "again without uninstalling; takes effect on the next shot, no restart needed.");

            Debug = Config.Bind(
                "2. Misc",
                "Debug logging",
                false,
                "Log clip loading and every bank swap to the BepInEx console. For troubleshooting only.");

            LogSoundEvents = Config.Bind(
                "2. Misc",
                "Log sound event names",
                false,
                "Print each weapon's reload and foley event names when it is drawn, and log those " +
                "events as they fire. These names are the keys a sound pack uses for non-fire " +
                "audio; they live on the weapon prefabs, so this is the only way to read them.");

            LogWeaponIds = Config.Bind(
                "2. Misc",
                "Log weapon template ids",
                false,
                "Print the template id of every weapon fired. This is what a pack's match block " +
                "needs when targeting one exact weapon. Logs on every shot, so turn it back off.");

            LoadPacks(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            new LocalFirePatch().Enable();
            new ObservedFirePatch().Enable();
            new SoundEventDumpPatch().Enable();
            new EventSoundPatch().Enable();

            if (LogWeaponIds.Value)
                Log.LogInfo("Weapon id logging is ON, fire any gun to see its template id here.");
        }

        // Packs are found by their manifest, so a third party ships a folder and nothing else.
        public static void LoadPacks(string pluginDir)
        {
            PackRegistry.Clear();
            ClipLibrary.Forget();

            string resoundRoot = Path.Combine(pluginDir, "Resound");

            Pack builtin = LegacyMapping.Load(pluginDir);
            if (builtin != null)
                PackRegistry.Add(builtin);

            PackRegistry.Discover(resoundRoot);
            PackRegistry.WarnMisplaced(pluginDir, resoundRoot);
            PackRegistry.Sort();

            int entries = 0;
            int clips = 0;

            foreach (Pack pack in PackRegistry.All)
            {
                int active = 0;
                HashSet<string> sets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (PackEntry e in pack.Entries)
                {
                    if (!e.Enabled || string.IsNullOrEmpty(e.SoundSet))
                        continue;

                    active++;
                    sets.Add(e.SoundSet);
                }

                int packClips = 0;
                foreach (string set in sets)
                    packClips += ClipLibrary.Scan(Path.Combine(Path.Combine(pack.Directory, "sounds"), set));

                entries += active;
                clips += packClips;

                Log.LogInfo("Pack '" + pack.Id + "'" +
                            (string.IsNullOrEmpty(pack.Name) ? "" : " (" + pack.Name + ")") +
                            (string.IsNullOrEmpty(pack.Author) ? "" : " by " + pack.Author) +
                            ": " + active + " entry(s), " + sets.Count + " set(s), " +
                            packClips + " clip(s), priority " + pack.Priority);
            }

            Log.LogInfo(NAME + " " + VERSION + " loaded " + PackRegistry.Count + " pack(s): " +
                        entries + " entry(s), " + clips + " clip(s).");

            // Ask the server last, once local packs are known: its answer names pack ids that
            // have to be matched against what is installed here.
            Dictionary<string, ResolvedSound> table = ServerSync.Fetch();
            if (table != null)
                PackRegistry.UseServerTable(table);

            BindPackSelector();
        }

        // Built from what is actually installed, so the dropdown lists the user's packs by name
        // rather than making them work out priority numbers.
        private static void BindPackSelector()
        {
            List<string> choices = new List<string> { PackRegistry.Auto };

            // The server's table already has the per-weapon pins baked in, so a local override
            // here would silently disagree with the config page.
            if (!PackRegistry.ServerDriven)
            {
                foreach (Pack pack in PackRegistry.All)
                    choices.Add(pack.Id);
            }

            ActivePack = Instance.Config.Bind(
                "1. General",
                "Active sound pack",
                PackRegistry.Auto,
                new ConfigDescription(
                    "Which pack to hear. 'Auto' uses every installed pack, highest priority first. " +
                    "Pick one by name and the others are ignored, though the sounds shipped with " +
                    "Resound still cover anything that pack does not. Takes effect on the next " +
                    "weapon draw, no restart.",
                    new AcceptableValueList<string>(choices.ToArray())));

            // A pack can be uninstalled while its name is still saved in the config file.
            if (ActivePack.Value != PackRegistry.Auto && !PackRegistry.Has(ActivePack.Value))
            {
                Log.LogWarning("Selected pack '" + ActivePack.Value + "' is not installed, using Auto.");
                ActivePack.Value = PackRegistry.Auto;
            }

            PackRegistry.Selected = ActivePack.Value;
            ActivePack.SettingChanged += delegate { PackRegistry.Selected = ActivePack.Value; };
        }

        public static void Dbg(string msg)
        {
            if (Debug != null && Debug.Value)
                Log.LogInfo(msg);
        }
    }
}
