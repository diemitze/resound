using System;
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

        // Reload and foley, keyed by BSG's animator event name (SndMagOut, SndBoltIn, …).
        public Dictionary<string, AudioClip[]> Events;

        public bool IsEmpty
        {
            get
            {
                return Body == null && Tail == null && Doublet == null &&
                       BodySilenced == null && TailSilenced == null && DoubletSilenced == null &&
                       (Events == null || Events.Count == 0);
            }
        }

        public AudioClip[] Event(string eventName)
        {
            if (Events == null || string.IsNullOrEmpty(eventName))
                return null;

            Events.TryGetValue(eventName, out AudioClip[] clips);
            return clips;
        }
    }

    internal static class ClipLibrary
    {
        private static readonly Dictionary<string, SoundSet> Cache =
            new Dictionary<string, SoundSet>(StringComparer.OrdinalIgnoreCase);

        // Counts files only. AudioClip.Create during plugin Awake returns zero-length
        // clips that cache as successful and play nothing, so clips are built on first use.
        public static int Scan(string setDir)
        {
            if (string.IsNullOrEmpty(setDir) || !Directory.Exists(setDir))
            {
                Plugin.Log.LogWarning("Sound set folder missing: " + setDir);
                return 0;
            }

            int count = Directory.GetFiles(setDir, "*.wav", SearchOption.AllDirectories).Length;
            if (count == 0)
                Plugin.Log.LogWarning("Sound set has no .wav files: " + setDir);

            return count;
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
            if (s == null || !Alive(s.Body) || !Alive(s.Tail) || !Alive(s.Doublet) ||
                !Alive(s.BodySilenced) || !Alive(s.TailSilenced) || !Alive(s.DoubletSilenced))
                return false;

            if (s.Events != null)
            {
                foreach (AudioClip[] clips in s.Events.Values)
                {
                    if (!Alive(clips))
                        return false;
                }
            }

            return true;
        }

        // Keyed on the full directory, not the set name, or two packs shipping a set called
        // m4a1 would silently share one another's clips.
        public static SoundSet Get(string setDir)
        {
            if (string.IsNullOrEmpty(setDir))
                return null;

            if (Cache.TryGetValue(setDir, out SoundSet cached))
            {
                if (Alive(cached))
                    return cached;

                Plugin.Log.LogWarning("Clips for '" + setDir + "' were unloaded by the engine; reloading.");
                Cache.Remove(setDir);
            }

            SoundSet set = null;
            try
            {
                set = LoadFrom(setDir);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Loading set '" + setDir + "' failed: " + e.Message);
            }

            // Only cache successes so a failed load can recover on the next shot.
            if (set != null)
                Cache[setDir] = set;

            return set;
        }

        public static void Forget()
        {
            Cache.Clear();
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
                DoubletSilenced = LoadFolder(Path.Combine(dir, "doublet_silenced")),
                Events = LoadEvents(Path.Combine(dir, "events"))
            };

            // Loose wavs in the set folder count as the body.
            if (set.Body == null)
                set.Body = LoadFolder(dir);

            // Fall back to unsuppressed, not vanilla, or fitting a can flips the gun back.
            // Tail is deliberately excluded: inheriting an unsuppressed tail puts a full-volume
            // crack after a quiet body, so vanilla's short suppressed tail stays unless a set
            // ships tail_silenced/ itself.
            if (set.BodySilenced == null) set.BodySilenced = set.Body;
            if (set.DoubletSilenced == null) set.DoubletSilenced = set.Doublet;

            if (set.IsEmpty)
            {
                Plugin.Log.LogWarning("Sound set has no usable .wav files: " + dir);
                return null;
            }

            return set;
        }

        // One subfolder per event name; the folder name is matched against SoundElement.EventName
        // verbatim, because BSG's names are per weapon class and sets are already per class.
        private static Dictionary<string, AudioClip[]> LoadEvents(string eventsDir)
        {
            if (!Directory.Exists(eventsDir))
                return null;

            Dictionary<string, AudioClip[]> events =
                new Dictionary<string, AudioClip[]>(StringComparer.OrdinalIgnoreCase);

            foreach (string sub in Directory.GetDirectories(eventsDir))
            {
                AudioClip[] clips = LoadFolder(sub);
                if (clips != null)
                    events[Path.GetFileName(sub)] = clips;
            }

            return events.Count == 0 ? null : events;
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
                UnityEngine.Object.DontDestroyOnLoad(clip);

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
