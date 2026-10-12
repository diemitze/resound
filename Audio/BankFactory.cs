using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;

namespace Resound.Audio
{
    // Clones a vanilla SoundBank and swaps only its clips, so BSG's tuning survives.
    internal static class BankFactory
    {
        private static readonly FieldInfo ShuffleField =
            typeof(SoundBank).GetField("_shuffle", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ShuffleIndexField =
            typeof(SoundBank).GetField("_shuffleIndex", BindingFlags.Instance | BindingFlags.NonPublic);

        // Shared like the vanilla assets are, otherwise every weapon draw leaks a clone.
        private static readonly Dictionary<string, SoundBank> Cache = new Dictionary<string, SoundBank>();

        // Kept vanilla banks still take the set's volume, or turning it down does nothing.
        public static SoundBank Rescale(SoundBank original, float volumeScale)
        {
            if (original == null || volumeScale == 1.0f)
                return null;

            string key = original.GetInstanceID() + "|rescale|" + volumeScale;
            if (Cache.TryGetValue(key, out SoundBank hit) && hit != null)
                return hit;

            SoundBank bank = Object.Instantiate(original);
            Object.DontDestroyOnLoad(bank);
            Cache[key] = bank;

            bank.BaseVolume *= volumeScale;
            bank.DeltaVolume *= volumeScale;
            bank.name = original.name + "_resound_vol";

            ShuffleField?.SetValue(bank, null);
            ShuffleIndexField?.SetValue(bank, (byte)0);

            return bank;
        }

        // buildLoop is only correct for an automatic weapon's Body bank.
        public static SoundBank Clone(SoundBank original, AudioClip[] clips, bool buildLoop, float volumeScale,
            float decayBeats, bool autoLevel)
        {
            if (original == null || clips == null || clips.Length == 0)
                return null;

            string key = original.GetInstanceID() + "|" + clips[0].GetInstanceID() + "|" + clips.Length +
                         "|" + buildLoop + "|" + volumeScale + "|" + decayBeats + "|" + autoLevel;
            if (Cache.TryGetValue(key, out SoundBank hit) && hit != null)
                return hit;

            if (buildLoop)
            {
                float loopLength = original.ClipLength;
                if (loopLength > 0f)
                {
                    AudioClip[] looped = new AudioClip[clips.Length];
                    for (int i = 0; i < clips.Length; i++)
                        looped[i] = LoopBuilder.Build(clips[i], loopLength, decayBeats);

                    clips = looped;
                }
            }

            // After looping, so a full-auto gun compares loop with loop.
            if (autoLevel)
                volumeScale *= Loudness.MatchScale(original, clips);

            SoundBank bank = Object.Instantiate(original);
            Object.DontDestroyOnLoad(bank);
            Cache[key] = bank;

            // DeltaVolume scales too, or the random spread dominates at low volumes.
            if (volumeScale != 1.0f)
            {
                bank.BaseVolume *= volumeScale;
                bank.DeltaVolume *= volumeScale;
            }
            bank.name = original.name + "_resound";

            if (bank.Environments != null)
            {
                foreach (EnvironmentVariety env in bank.Environments)
                {
                    if (env?.Clips == null)
                        continue;

                    foreach (DistanceVarity dist in env.Clips)
                    {
                        if (dist != null)
                            dist.Clips = clips;
                    }
                }
            }

            // Shuffle() never shrinks its bag, so stale indices would throw on a shorter clip array.
            ShuffleField?.SetValue(bank, null);
            ShuffleIndexField?.SetValue(bank, (byte)0);

            float longest = 0f;
            foreach (AudioClip c in clips)
            {
                if (c != null && c.length > longest)
                    longest = c.length;
            }

            // ClipLength prefers CustomLength, and full-auto beat timing reads it.
            if (longest > 0f)
            {
                bank.CustomLength = longest;
            }
            else
            {
                Plugin.Log.LogWarning("Replacement clips for '" + original.name +
                                      "' have no length, they were probably unloaded.");
            }

            return bank;
        }
    }
}
