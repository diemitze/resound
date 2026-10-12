using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Resound.Audio;

namespace Resound.Config
{
    /// <summary>Writes the pack health check to a file, because the console scrolls past it during a load.</summary>
    internal static class PackReport
    {
        public const string FileName = "pack-report.txt";

        private static readonly string[] Slots =
        {
            "body", "tail", "doublet", "body_silenced", "tail_silenced", "doublet_silenced"
        };

        // Extensions people try to ship. Readmes and licences beside the audio are left alone.
        private static readonly string[] AudioExtensions =
        {
            ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".opus", ".wv", ".vsnd"
        };

        public static void Write(string resoundRoot)
        {
            string path = Path.Combine(resoundRoot, FileName);
            int problems = 0;

            try
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("Resound " + Plugin.VERSION + " pack report");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Packs are read from " + resoundRoot);
                sb.AppendLine();
                sb.AppendLine("Lines marked PROBLEM are why a weapon is still playing its vanilla sound.");
                sb.AppendLine("Lines marked NOTE are fine but worth knowing. This file is rewritten every launch.");
                sb.AppendLine();

                foreach (Pack pack in PackRegistry.All)
                    problems += WritePack(sb, pack);

                if (PackRegistry.Count == 0)
                    sb.AppendLine("No packs are installed at all, not even the built-in sounds.");

                sb.AppendLine();
                sb.AppendLine(problems == 0
                    ? "No problems found."
                    : problems + " problem(s) found, listed above.");

                Directory.CreateDirectory(resoundRoot);
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write the pack report: " + e.Message);
                return;
            }

            if (problems > 0)
                Plugin.Log.LogWarning(problems + " problem(s) with the installed sound packs. " +
                                      "Details in " + path);
            else
                Plugin.Dbg("Pack report written to " + path);
        }

        private static int WritePack(StringBuilder sb, Pack pack)
        {
            int problems = 0;

            sb.AppendLine("=================================================================");
            sb.AppendLine(pack.Id +
                          (string.IsNullOrEmpty(pack.Name) ? "" : "  (" + pack.Name + ")") +
                          (string.IsNullOrEmpty(pack.Author) ? "" : "  by " + pack.Author));
            sb.AppendLine("  folder    " + pack.Directory);
            sb.AppendLine("  priority  " + pack.Priority +
                          (pack.Priority == LegacyMapping.Priority ? "  (the floor, any pack you install outranks it)" : ""));
            sb.AppendLine("  autoLevel " + (pack.AutoLevel ? "on, clip levels are matched to vanilla for you"
                                                          : "off, your volume numbers are used as-is"));
            sb.AppendLine();

            string soundsRoot = Path.Combine(pack.Directory, "sounds");

            if (!Directory.Exists(soundsRoot))
            {
                sb.AppendLine("  PROBLEM  there is no 'sounds' folder here. Every soundSet is a folder");
                sb.AppendLine("           under " + soundsRoot);
                sb.AppendLine();
                return 1;
            }

            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackEntry entry in pack.Entries)
                problems += WriteEntry(sb, entry, soundsRoot, used);

            problems += WriteUnusedSets(sb, soundsRoot, used);

            sb.AppendLine();
            return problems;
        }

        private static int WriteEntry(StringBuilder sb, PackEntry entry, string soundsRoot, HashSet<string> used)
        {
            string label = string.IsNullOrEmpty(entry.Name) ? entry.SoundSet : entry.Name;
            if (string.IsNullOrEmpty(label))
                label = "(unnamed entry)";

            if (!entry.Enabled)
            {
                sb.AppendLine("  \"" + label + "\"  NOTE  disabled, skipped entirely");
                sb.AppendLine();
                return 0;
            }

            if (entry.Match == null || entry.Match.IsEmpty)
            {
                sb.AppendLine("  \"" + label + "\"");
                sb.AppendLine("    PROBLEM  no match block, so this entry claims no weapons. Give it a");
                sb.AppendLine("             \"match\" with a weapClass, templateId or namePattern.");
                sb.AppendLine();
                return 1;
            }

            if (string.IsNullOrEmpty(entry.SoundSet))
            {
                sb.AppendLine("  \"" + label + "\"");
                sb.AppendLine("    PROBLEM  no soundSet, so there is nothing to play.");
                sb.AppendLine();
                return 1;
            }

            used.Add(entry.SoundSet);

            string setDir = Path.Combine(soundsRoot, entry.SoundSet);
            sb.AppendLine("  \"" + label + "\"  ->  sounds/" + entry.SoundSet +
                          "   [specificity " + entry.Match.Specificity + "]");

            if (!Directory.Exists(setDir))
            {
                sb.AppendLine("    PROBLEM  no folder called '" + entry.SoundSet + "' under " + soundsRoot);

                string near = Nearest(soundsRoot, entry.SoundSet);
                if (near != null)
                    sb.AppendLine("             did you mean '" + near + "'? Folder names are matched exactly.");

                sb.AppendLine();
                return 1;
            }

            int problems = 0;
            bool anyClips = false;

            foreach (string slot in Slots)
            {
                string dir = Path.Combine(setDir, slot);

                // Loose wavs at the top of the set folder count as the body.
                if (slot == "body" && !Directory.Exists(dir) && Directory.Exists(setDir))
                {
                    problems += WriteSlot(sb, "body (loose files)", setDir, ref anyClips, false);
                    continue;
                }

                problems += WriteSlot(sb, slot, dir, ref anyClips, true);
            }

            problems += WriteEvents(sb, Path.Combine(setDir, "events"), ref anyClips);

            if (!anyClips)
            {
                sb.AppendLine("    PROBLEM  this set has no usable audio at all, so the weapon stays vanilla.");
                problems++;
            }

            sb.AppendLine();
            return problems;
        }

        private static int WriteEvents(StringBuilder sb, string eventsDir, ref bool anyClips)
        {
            if (!Directory.Exists(eventsDir))
                return 0;

            string[] subs;
            try
            {
                subs = Directory.GetDirectories(eventsDir);
            }
            catch (Exception e)
            {
                sb.AppendLine("    PROBLEM  could not read the events folder: " + e.Message);
                return 1;
            }

            if (subs.Length == 0)
            {
                sb.AppendLine("    PROBLEM  events/ is empty. Reload sounds go in a subfolder named after");
                sb.AppendLine("             the animation event, e.g. events/SndMagOut/. Turn on");
                sb.AppendLine("             'Log sound event names' and draw the weapon to see the real list.");
                return 1;
            }

            int problems = 0;
            foreach (string sub in subs)
            {
                string label = "events/" + Path.GetFileName(sub);

                // An empty slot folder keeps vanilla on purpose; an empty event folder never does.
                if (!HasAudio(sub))
                {
                    sb.AppendLine("    " + Pad(label) + "PROBLEM  no audio files in here, so this " +
                                  "event stays vanilla.");
                    problems++;
                    continue;
                }

                problems += WriteSlot(sb, label, sub, ref anyClips, true);
            }

            return problems;
        }

        private static bool HasAudio(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".wav" || Array.IndexOf(AudioExtensions, ext) >= 0)
                        return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        // Not required at the set root: body clips usually live in body/.
        private static int WriteSlot(StringBuilder sb, string label, string dir, ref bool anyClips, bool required)
        {
            if (!Directory.Exists(dir))
                return 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception e)
            {
                sb.AppendLine("    " + Pad(label) + "PROBLEM  could not be read: " + e.Message);
                return 1;
            }

            List<string> wavs = new List<string>();
            List<string> wrongFormat = new List<string>();

            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();

                if (ext == ".wav")
                    wavs.Add(f);
                else if (Array.IndexOf(AudioExtensions, ext) >= 0)
                    wrongFormat.Add(f);
            }

            if (wavs.Count == 0 && wrongFormat.Count == 0)
                return 0;

            int problems = 0;
            int good = 0;
            List<string> lines = new List<string>();

            foreach (string f in wavs)
            {
                WavInfo info = WavLoader.Inspect(f);
                string name = Path.GetFileName(f);

                if (!info.Ok)
                {
                    lines.Add("      PROBLEM  " + name + ": " + info.Problem);
                    problems++;
                    continue;
                }

                good++;

                if (info.Note != null)
                    lines.Add("      NOTE     " + name + ": " + info.Note);
            }

            foreach (string f in wrongFormat)
            {
                lines.Add("      PROBLEM  " + Path.GetFileName(f) + ": Resound only reads WAV, so this file " +
                          "is ignored.");
                lines.Add("               Convert it: ffmpeg -i \"" + Path.GetFileName(f) +
                          "\" -ac 1 -ar 48000 -c:a pcm_s16le \"" +
                          Path.GetFileNameWithoutExtension(f) + ".wav\"");
                problems++;
            }

            sb.AppendLine("    " + Pad(label) + good + " clip(s)" +
                          (problems > 0 ? "   " + problems + " file(s) will not load" : ""));

            foreach (string line in lines)
                sb.AppendLine(line);

            if (good > 0)
                anyClips = true;
            else if (required)
                sb.AppendLine("      PROBLEM  nothing here loads, so this slot keeps its vanilla sound.");

            return problems;
        }

        // An orphaned set folder is usually a soundSet typo, and otherwise invisible.
        private static int WriteUnusedSets(StringBuilder sb, string soundsRoot, HashSet<string> used)
        {
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(soundsRoot);
            }
            catch (Exception)
            {
                return 0;
            }

            List<string> orphans = new List<string>();
            foreach (string d in dirs)
            {
                string name = Path.GetFileName(d);
                if (!used.Contains(name))
                    orphans.Add(name);
            }

            if (orphans.Count == 0)
                return 0;

            sb.AppendLine("  PROBLEM  " + orphans.Count + " sound set folder(s) that no entry uses, so nothing");
            sb.AppendLine("           in them is ever heard: " + string.Join(", ", orphans.ToArray()));
            sb.AppendLine("           Either add an entry with that soundSet, or delete the folder.");
            sb.AppendLine();

            return 1;
        }

        // Case and separators are the usual slip, so say so outright.
        private static string Nearest(string soundsRoot, string wanted)
        {
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(soundsRoot);
            }
            catch (Exception)
            {
                return null;
            }

            string squashed = Squash(wanted);

            foreach (string d in dirs)
            {
                string name = Path.GetFileName(d);
                if (Squash(name) == squashed)
                    return name;
            }

            return null;
        }

        private static string Squash(string s)
        {
            return s == null ? "" : s.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        }

        private static string Pad(string label)
        {
            return label.Length >= 22 ? label + "  " : label.PadRight(22);
        }
    }
}
