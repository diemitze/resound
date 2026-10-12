using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Resound.Audio
{
    // Matches replacement clips to the vanilla level, since source games master gunshots far hotter than BSG.
    internal static class Loudness
    {
        // Loudest window, not whole-file RMS, so a long quiet ring does not read as a quieter shot.
        private const float WindowSeconds = 0.12f;

        // Outside this it is a broken or misfiled clip, not mastering; clamping keeps it audible.
        private const float MinScale = 0.1f;
        private const float MaxScale = 6f;

        private static readonly Dictionary<int, float> ClipLevels = new Dictionary<int, float>();
        private static readonly Dictionary<string, float> Scales = new Dictionary<string, float>();

        private static bool _warned;

        public static void Forget()
        {
            ClipLevels.Clear();
            Scales.Clear();
            _warned = false;
        }

        /// <summary>Scale that brings clips to the level of the bank they replace, or 1 when either side cannot be measured.</summary>
        public static float MatchScale(SoundBank vanilla, AudioClip[] clips)
        {
            if (vanilla == null || clips == null || clips.Length == 0)
                return 1f;

            string key = vanilla.GetInstanceID() + "|" + clips[0].GetInstanceID() + "|" + clips.Length;
            if (Scales.TryGetValue(key, out float cached))
                return cached;

            float target = Average(BankClips(vanilla));
            float mine = Average(clips);
            float scale = 1f;

            if (target > 0f && mine > 0f)
            {
                scale = Mathf.Clamp(target / mine, MinScale, MaxScale);

                Plugin.Dbg("Auto level " + vanilla.name + ": vanilla " + Db(target) + " dB, replacement " +
                           Db(mine) + " dB, scale " + scale.ToString("F2") +
                           (scale <= MinScale || scale >= MaxScale ? " (clamped)" : ""));
            }
            else if (!_warned)
            {
                _warned = true;
                Plugin.Log.LogWarning("Could not measure the vanilla level of '" + vanilla.name +
                                      "', so replacement clips there play at their own level. " +
                                      "Set bodyVolume and tailVolume by hand if a weapon comes " +
                                      "out too loud or too quiet.");
            }

            Scales[key] = scale;
            return scale;
        }

        // Mean level, so one hot alternate take nudges the answer rather than deciding it.
        private static float Average(IEnumerable<AudioClip> clips)
        {
            if (clips == null)
                return 0f;

            float sum = 0f;
            int n = 0;

            foreach (AudioClip clip in clips)
            {
                float level = Level(clip);
                if (level <= 0f)
                    continue;

                sum += level;
                n++;
            }

            return n == 0 ? 0f : sum / n;
        }

        // Peak windowed RMS, or 0 when the clip's samples are not readable.
        private static float Level(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
                return 0f;

            int id = clip.GetInstanceID();
            if (ClipLevels.TryGetValue(id, out float cached))
                return cached;

            float level = 0f;

            // BSG ships PCM marked CompressedInMemory, which reads fine; truly compressed clips land on 0 and are skipped.
            if (clip.loadType == AudioClipLoadType.Streaming)
            {
                ClipLevels[id] = 0f;
                return 0f;
            }

            // Nothing is loaded before the first shot; loading an in-memory clip is synchronous.
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();

            // Still loading in the background: measure next time rather than cache a zero.
            if (clip.loadState != AudioDataLoadState.Loaded)
                return 0f;

            try
            {
                float[] data = new float[clip.samples * clip.channels];
                if (clip.GetData(data, 0))
                    level = PeakWindowRms(data, clip.channels, clip.frequency);
            }
            catch (Exception e)
            {
                Plugin.Dbg("Could not read samples from " + clip.name + ": " + e.Message);
            }

            ClipLevels[id] = level;
            return level;
        }

        private static float PeakWindowRms(float[] interleaved, int channels, int frequency)
        {
            int frames = interleaved.Length / channels;
            if (frames <= 0)
                return 0f;

            int window = Mathf.Clamp(Mathf.RoundToInt(WindowSeconds * frequency), 1, frames);

            double sum = 0.0;
            double best = 0.0;

            for (int f = 0; f < frames; f++)
            {
                sum += Square(interleaved, f, channels);

                if (f >= window)
                    sum -= Square(interleaved, f - window, channels);

                if (f >= window - 1 && sum > best)
                    best = sum;
            }

            return (float)Math.Sqrt(best / window);
        }

        // Folded the same way the loader folds ours, so both sides mean the same thing.
        private static double Square(float[] interleaved, int frame, int channels)
        {
            if (channels == 1)
            {
                double v = interleaved[frame];
                return v * v;
            }

            double sum = 0.0;
            int b = frame * channels;
            for (int c = 0; c < channels; c++)
                sum += interleaved[b + c];

            sum /= channels;
            return sum * sum;
        }

        private static IEnumerable<AudioClip> BankClips(SoundBank bank)
        {
            if (bank == null || bank.Environments == null)
                yield break;

            foreach (EnvironmentVariety env in bank.Environments)
            {
                if (env?.Clips == null)
                    continue;

                foreach (DistanceVarity dist in env.Clips)
                {
                    if (dist?.Clips == null)
                        continue;

                    foreach (AudioClip clip in dist.Clips)
                    {
                        if (clip != null)
                            yield return clip;
                    }
                }

                // One environment is enough; the others repeat the same samples.
                yield break;
            }
        }

        private static string Db(float amplitude)
        {
            return amplitude <= 0f ? "-inf" : (20f * Mathf.Log10(amplitude)).ToString("F1");
        }
    }
}
