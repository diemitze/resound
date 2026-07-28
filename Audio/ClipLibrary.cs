using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Resound.Audio
{
    // A null slot means "keep vanilla", so a body-only set still gets EFT's tail.
    internal class SoundSet
    {
        public AudioClip[] Body;
        public AudioClip[] Tail;
        public AudioClip[] Doublet;
        public AudioClip[] BodySilenced;
        public AudioClip[] TailSilenced;
        public AudioClip[] DoubletSilenced;

        public bool IsEmpty
        {
            get
            {
                return Body == null && Tail == null && Doublet == null &&
                       BodySilenced == null && TailSilenced == null && DoubletSilenced == null;
            }
        }
    }

    internal static class ClipLibrary
    {
        private static readonly Dictionary<string, SoundSet> Cache =
            new Dictionary<string, SoundSet>(System.StringComparer.OrdinalIgnoreCase);

        // Counts files only. AudioClip.Create during plugin Awake returns zero-length
        // clips that cache as successful and play nothing, so clips are built on first use.
        public static void Scan(string soundsRoot, IEnumerable<string> setNames)
        {
            foreach (string name in setNames)
            {
                if (string.IsNullOrEmpty(name))
                    continue;

                string dir = Path.Combine(soundsRoot, name);
                if (!Directory.Exists(dir))
                {
                    Plugin.Log.LogWarning("Sound set folder missing: " + dir);
                    continue;
                }

                int count = Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories).Length;
                if (count == 0)
                    Plugin.Log.LogWarning("Sound set '" + name + "' has no .wav files: " + dir);
                else
                    Plugin.Log.LogInfo("Found set '" + name + "': " + count + " .wav file(s)");
            }
        }

        // A dead clip is a "fake null" the play path silently drops, so check rather than trust.
        private static bool Alive(AudioClip[] clips)
        {
            if (clips == null)
                return true;

            foreach (AudioClip c in clips)
            {
                if (c == null || c.length <= 0f)
                    return false;
            }

            return true;
        }

        private static bool Alive(SoundSet s)
        {
            return s != null && Alive(s.Body) && Alive(s.Tail) && Alive(s.Doublet) &&
                   Alive(s.BodySilenced) && Alive(s.TailSilenced) && Alive(s.DoubletSilenced);
        }

        public static SoundSet Get(string soundsRoot, string setName)
        {
            if (Cache.TryGetValue(setName, out SoundSet cached))
            {
                if (Alive(cached))
                    return cached;

                Plugin.Log.LogWarning("Clips for '" + setName + "' were unloaded by the engine; reloading.");
                Cache.Remove(setName);
            }

            SoundSet set = null;
            try
            {
                set = LoadFrom(Path.Combine(soundsRoot, setName));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Loading set '" + setName + "' failed: " + e.Message);
            }

            // Only cache successes so a failed load can recover on the next shot.
            if (set != null)
                Cache[setName] = set;

            return set;
        }

        private static SoundSet LoadFrom(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Plugin.Log.LogWarning("Sound set folder missing: " + dir);
                return null;
            }

            SoundSet set = new SoundSet
            {
                Body = LoadFolder(Path.Combine(dir, "body")),
                Tail = LoadFolder(Path.Combine(dir, "tail")),
                Doublet = LoadFolder(Path.Combine(dir, "doublet")),
                BodySilenced = LoadFolder(Path.Combine(dir, "body_silenced")),
                TailSilenced = LoadFolder(Path.Combine(dir, "tail_silenced")),
                DoubletSilenced = LoadFolder(Path.Combine(dir, "doublet_silenced"))
            };

            // Loose wavs in the set folder count as the body.
            if (set.Body == null)
                set.Body = LoadFolder(dir);

            // Fall back to unsuppressed, not vanilla, or fitting a can flips the gun back.
            if (set.BodySilenced == null) set.BodySilenced = set.Body;
            if (set.TailSilenced == null) set.TailSilenced = set.Tail;
            if (set.DoubletSilenced == null) set.DoubletSilenced = set.Doublet;

            if (set.IsEmpty)
            {
                Plugin.Log.LogWarning("Sound set has no usable .wav files: " + dir);
                return null;
            }

            return set;
        }

        private static AudioClip[] LoadFolder(string dir)
        {
            if (!Directory.Exists(dir))
                return null;

            string[] files = Directory.GetFiles(dir, "*.wav", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
                return null;

            List<AudioClip> clips = new List<AudioClip>(files.Length);
            foreach (string f in files)
            {
                AudioClip clip = WavLoader.Load(f);
                if (clip == null)
                    continue;

                // Assets are not scene-referenced, so UnloadUnusedAssets would collect them.
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Object.DontDestroyOnLoad(clip);

                if (clip.length <= 0f)
                {
                    Plugin.Log.LogWarning("Clip built with zero length (audio system not ready?): " + f);
                    continue;
                }

                clips.Add(clip);
            }

            if (clips.Count == 0)
                return null;

            Plugin.Dbg("Loaded " + clips.Count + " clip(s) from " + dir);
            return clips.ToArray();
        }
    }
}
