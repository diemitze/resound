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
        public const string VERSION = "1.0.0";

        public static ManualLogSource Log;
        internal static SoundMap Map;
        public static string SoundsRoot;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> Debug;

        private void Awake()
        {
            Log = Logger;

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

            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            SoundsRoot = Path.Combine(pluginDir, "Resound", "sounds");

            Map = SoundMap.Load(Path.Combine(SoundsRoot, "mapping.json"));

            // Weapons share sets, so dedupe or the startup log repeats itself.
            HashSet<string> sets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            int active = 0;
            foreach (WeaponMapping w in Map.Weapons)
            {
                if (!w.Enabled || string.IsNullOrEmpty(w.SoundSet))
                    continue;

                sets.Add(w.SoundSet);
                active++;
            }

            ClipLibrary.Scan(SoundsRoot, sets);

            new LocalFirePatch().Enable();
            new ObservedFirePatch().Enable();

            Log.LogInfo(NAME + " " + VERSION + " loaded: " + active + " of " + Map.Weapons.Count +
                        " mapping(s) active across " + sets.Count + " sound set(s).");

            if (Map.LogWeaponIds)
                Log.LogInfo("logWeaponIds is ON, fire any gun to see its template id here.");
        }

        
        public static void Dbg(string msg)
        {
            if (Debug != null && Debug.Value)
                Log.LogInfo(msg);
        }
    }
}

