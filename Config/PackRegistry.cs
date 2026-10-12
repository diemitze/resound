using System;
using System.Collections.Generic;
using System.IO;
using EFT.InventoryLogic;
using Newtonsoft.Json;

namespace Resound.Config
{
    // Finds sound packs, decides which one owns each weapon, and caches the answer.
    internal static class PackRegistry
    {
        // Auto lets every pack compete; naming one is "I just want this pack".
        public const string Auto = "Auto";

        private const string ManifestName = "resound.pack.json";

        // Depth is for a nested layout inside one pack's folder, not for searching all of plugins/.
        private const int MaxDepth = 3;

        // The built-in audio, hundreds of folders and never a manifest.
        private const string BuiltinSounds = "sounds";

        private static readonly List<Pack> Packs = new List<Pack>();

        private static readonly Dictionary<string, SoundProfile> Resolved =
            new Dictionary<string, SoundProfile>(StringComparer.OrdinalIgnoreCase);

        // The server's answer wins outright: same rules plus the config page's pins.
        private static Dictionary<string, ResolvedSound> _server;

        private static string _selected = Auto;

        public static string Selected
        {
            get { return _selected; }
            set
            {
                if (string.Equals(_selected, value, StringComparison.OrdinalIgnoreCase))
                    return;

                _selected = string.IsNullOrEmpty(value) ? Auto : value;

                // Only the decision is stale, not the audio, so keep the loaded clips.
                Resolved.Clear();
                Plugin.Log.LogInfo("Active sound pack set to '" + _selected + "'.");
            }
        }

        public static bool ServerDriven
        {
            get { return _server != null; }
        }

        public static void UseServerTable(Dictionary<string, ResolvedSound> map)
        {
            _server = map;
            Resolved.Clear();
        }

        private static Pack FindPack(string packId)
        {
            foreach (Pack p in Packs)
            {
                if (string.Equals(p.Id, packId, StringComparison.OrdinalIgnoreCase))
                    return p;
            }

            return null;
        }

        public static bool Has(string packId)
        {
            return FindPack(packId) != null;
        }

        public static IEnumerable<Pack> All
        {
            get { return Packs; }
        }

        public static int Count
        {
            get { return Packs.Count; }
        }

        public static void Add(Pack pack)
        {
            if (pack == null)
                return;

            Packs.Add(pack);
            Resolved.Clear();
        }

        public static void Clear()
        {
            Packs.Clear();
            Resolved.Clear();
        }

        // One known folder, so startup never walks the whole plugins tree.
        public static void Discover(string resoundRoot)
        {
            foreach (string manifest in FindManifests(resoundRoot, 0))
            {
                Pack pack = Load(manifest);
                if (pack != null)
                    Add(pack);
            }
        }

        // Packs dropped straight into plugins/ used to load, so name them rather than go quiet.
        public static void WarnMisplaced(string pluginsRoot, string resoundRoot)
        {
            string[] subs;
            try
            {
                subs = Directory.GetDirectories(pluginsRoot);
            }
            catch (Exception)
            {
                return;
            }

            foreach (string sub in subs)
            {
                if (string.Equals(sub, resoundRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.Exists(Path.Combine(sub, ManifestName)))
                    Plugin.Log.LogWarning("Sound pack at " + sub + " is not being loaded. Packs go in " +
                                          resoundRoot + "; move the folder there.");
            }
        }

        private static IEnumerable<string> FindManifests(string dir, int depth)
        {
            if (depth > MaxDepth || !Directory.Exists(dir))
                yield break;

            string candidate = Path.Combine(dir, ManifestName);
            if (File.Exists(candidate))
            {
                // A manifest claims its whole folder, so stop rather than nest packs.
                yield return candidate;
                yield break;
            }

            string[] subs;
            try
            {
                subs = Directory.GetDirectories(dir);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not scan " + dir + ": " + e.Message);
                yield break;
            }

            foreach (string sub in subs)
            {
                if (depth == 0 && string.Equals(Path.GetFileName(sub), BuiltinSounds, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (string found in FindManifests(sub, depth + 1))
                    yield return found;
            }
        }

        private static Pack Load(string manifestPath)
        {
            try
            {
                Pack pack = JsonConvert.DeserializeObject<Pack>(File.ReadAllText(manifestPath));
                if (pack == null)
                {
                    Plugin.Log.LogWarning("Empty pack manifest: " + manifestPath);
                    return null;
                }

                pack.Directory = Path.GetDirectoryName(manifestPath);

                if (string.IsNullOrEmpty(pack.Id))
                {
                    // Folder name is a workable fallback and keeps load order deterministic.
                    pack.Id = Path.GetFileName(pack.Directory);
                    Plugin.Log.LogWarning("Pack at " + pack.Directory + " has no id, using '" + pack.Id + "'.");
                }

                if (pack.ApiVersion > Pack.SupportedApiVersion)
                {
                    Plugin.Log.LogWarning("Pack '" + pack.Id + "' needs Resound apiVersion " + pack.ApiVersion +
                                          " but this build supports " + Pack.SupportedApiVersion + ". Skipped.");
                    return null;
                }

                return pack;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Bad pack manifest " + manifestPath + ": " + e.Message);
                return null;
            }
        }

        // Alphabetical by id breaks ties the same way on every machine; filesystem order does not.
        public static void Sort()
        {
            Packs.Sort(delegate (Pack a, Pack b)
            {
                int p = b.Priority.CompareTo(a.Priority);
                return p != 0 ? p : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });

            Resolved.Clear();
        }

        private static SoundProfile FromServer(string templateId)
        {
            if (!_server.TryGetValue(templateId, out ResolvedSound r) || r == null)
                return null;

            Pack pack = FindPack(r.PackId);
            if (pack == null)
            {
                // Under Fika the server may name a pack this player has not installed.
                Plugin.Log.LogWarning("Server picked pack '" + r.PackId + "' for " + templateId +
                                      ", which is not installed here. Staying vanilla.");
                return null;
            }

            SoundProfile profile = new SoundProfile
            {
                Label = r.PackId + ":" + (string.IsNullOrEmpty(r.EntryName) ? r.SoundSet : r.EntryName),
                SetDir = Path.Combine(Path.Combine(pack.Directory, "sounds"), r.SoundSet),
                BodyVolume = r.BodyVolume,
                TailVolume = r.TailVolume,
                BodySilencedVolume = r.BodySilencedVolume,
                TailSilencedVolume = r.TailSilencedVolume,
                EventVolume = r.EventVolume,
                DecayBeats = r.DecayBeats,
                AutoLevel = r.AutoLevel
            };

            Plugin.Log.LogInfo("Resolved " + templateId + " -> " + profile.Label + " (server)");
            return profile;
        }

        public static SoundProfile Resolve(Weapon weapon)
        {
            if (weapon == null)
                return null;

            string templateId = weapon.TemplateId.ToString();
            if (Resolved.TryGetValue(templateId, out SoundProfile cached))
                return cached;

            if (_server != null)
            {
                SoundProfile fromServer = FromServer(templateId);
                Resolved[templateId] = fromServer;
                return fromServer;
            }

            WeaponFacts facts = WeaponFacts.From(weapon);
            Pack bestPack = null;
            PackEntry bestEntry = null;
            int bestPriority = int.MinValue;
            int bestScore = -1;
            bool builtinMatched = false;

            // Naming a pack never sidelines the built-ins, or six covered guns would drop the rest to vanilla.
            bool restricted = !string.Equals(_selected, Auto, StringComparison.OrdinalIgnoreCase);

            foreach (Pack pack in Packs)
            {
                if (restricted &&
                    !string.Equals(pack.Id, _selected, StringComparison.OrdinalIgnoreCase) &&
                    pack.Id != LegacyMapping.PackId)
                    continue;

                foreach (PackEntry entry in pack.Entries)
                {
                    if (!entry.Enabled || string.IsNullOrEmpty(entry.SoundSet) ||
                        entry.Match == null || entry.Match.IsEmpty || !entry.Match.Matches(facts))
                        continue;

                    if (pack.Id == LegacyMapping.PackId)
                        builtinMatched = true;

                    // Priority beats specificity, so an installed pack wins a gun the built-ins name exactly. Ties keep the earlier pack.
                    int score = entry.Match.Specificity;
                    if (pack.Priority < bestPriority ||
                        (pack.Priority == bestPriority && score <= bestScore))
                        continue;

                    bestPriority = pack.Priority;
                    bestScore = score;
                    bestPack = pack;
                    bestEntry = entry;
                }
            }

            SoundProfile best = null;

            if (bestEntry != null)
            {
                best = new SoundProfile
                {
                    Label = bestPack.Id + ":" + (string.IsNullOrEmpty(bestEntry.Name) ? bestEntry.SoundSet : bestEntry.Name),
                    SetDir = Path.Combine(Path.Combine(bestPack.Directory, "sounds"), bestEntry.SoundSet),
                    BodyVolume = bestEntry.BodyVolume,
                    TailVolume = bestEntry.TailVolume,
                    BodySilencedVolume = bestEntry.BodySilencedVolume,
                    TailSilencedVolume = bestEntry.TailSilencedVolume,
                    EventVolume = bestEntry.EventVolume,
                    DecayBeats = bestEntry.DecayBeats,
                    AutoLevel = bestEntry.AutoLevel ?? bestPack.AutoLevel
                };

                string note = builtinMatched && bestPack.Id != LegacyMapping.PackId
                    ? " (overrides built-in)"
                    : "";

                Plugin.Log.LogInfo("Resolved " + (facts.DisplayName ?? templateId) + " (" + templateId + ") -> " +
                                   best.Label + " [priority " + bestPriority + ", score " + bestScore + "]" + note);
            }

            Resolved[templateId] = best;
            return best;
        }
    }
}
