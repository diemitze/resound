using UnityEngine;

namespace Resound.Audio
{
    // An automatic weapon's Body bank is a 16-shot firing loop, not a single shot
    // (BeatLn is _firingLoopLength / 16). Rebuilds one from a single-shot sample,
    // taking the length from the bank being replaced so the rate matches the weapon.
    internal static class LoopBuilder
    {
        public const int Beats = 16;

        // Longer overlap turns a 1.2s sample into mush at a 0.09s beat.
        private const float DecayBeats = 4f;

        public static AudioClip Build(AudioClip source, float loopLength)
        {
            if (source == null || loopLength <= 0f)
                return source;

            int channels = source.channels;
            int freq = source.frequency;

            int totalFrames = Mathf.RoundToInt(loopLength * freq);
            if (totalFrames <= 0 || channels <= 0)
                return source;

            float[] src = new float[source.samples * channels];
            if (!source.GetData(src, 0))
                return source;

            int srcFrames = source.samples;
            int beatFrames = totalFrames / Beats;
            if (beatFrames <= 0)
                return source;

            int decayFrames = Mathf.Min(srcFrames, Mathf.RoundToInt(DecayBeats * beatFrames));
            if (decayFrames <= 0)
                return source;

            float[] dst = new float[totalFrames * channels];

            for (int b = 0; b < Beats; b++)
            {
                int start = b * beatFrames;

                for (int f = 0; f < decayFrames; f++)
                {
                    float gain = 1f - (float)f / decayFrames;

                    // Wrap so the last shot bleeds into the start when the loop repeats.
                    int dstFrame = (start + f) % totalFrames;

                    for (int c = 0; c < channels; c++)
                        dst[dstFrame * channels + c] += src[f * channels + c] * gain;
                }
            }

            // Normalise rather than clip, 16 overlapping copies will exceed full scale.
            float peak = 0f;
            for (int i = 0; i < dst.Length; i++)
            {
                float a = dst[i] < 0f ? -dst[i] : dst[i];
                if (a > peak)
                    peak = a;
            }

            if (peak > 0.95f)
            {
                float scale = 0.95f / peak;
                for (int i = 0; i < dst.Length; i++)
                    dst[i] *= scale;
            }

            AudioClip loop = AudioClip.Create(source.name + "_loop16", totalFrames, channels, freq, false);
            loop.SetData(dst, 0);
            loop.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(loop);

            Plugin.Dbg("Built 16-beat loop from " + source.name + ": " + loopLength + "s @ " +
                       (60f / (loopLength / Beats)).ToString("F0") + " RPM");

            return loop;
        }
    }
}
