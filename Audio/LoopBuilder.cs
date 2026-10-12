using UnityEngine;

namespace Resound.Audio
{
    // Builds the 16-shot firing loop an automatic Body bank expects, at the replaced bank's length.
    internal static class LoopBuilder
    {
        public const int Beats = 16;

        // Longer overlap turns a 1.2s sample into mush at a 0.09s beat.
        public const float DefaultDecayBeats = 4f;

        // Negative rather than 0, so an explicit 0 in a manifest is not read as auto.
        public const float Auto = -1f;

        private const float MinAutoBeats = 2f;
        private const float MaxAutoBeats = 8f;

        // Only the truncated end needs a ramp, and only enough of one to kill the click.
        private const float FadeFraction = 0.25f;

        /// <summary>Beats to keep when the manifest does not say: longer for long samples, capped at both ends.</summary>
        public static float Suggest(AudioClip source, float loopLength)
        {
            if (source == null || loopLength <= 0f)
                return DefaultDecayBeats;

            float beat = loopLength / Beats;
            if (beat <= 0f)
                return DefaultDecayBeats;

            return Mathf.Clamp(source.length / beat, MinAutoBeats, MaxAutoBeats);
        }

        public static AudioClip Build(AudioClip source, float loopLength, float decayBeats)
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

            if (decayBeats <= 0f)
                decayBeats = Suggest(source, loopLength);

            int decayFrames = Mathf.Min(srcFrames, Mathf.RoundToInt(decayBeats * beatFrames));
            if (decayFrames <= 0)
                return source;

            // A sample that fits its window is not cut, so it keeps its own decay.
            bool truncated = decayFrames < srcFrames;
            int fadeFrames = truncated ? Mathf.Max(1, Mathf.RoundToInt(decayFrames * FadeFraction)) : 0;
            int fadeStart = decayFrames - fadeFrames;

            float[] dst = new float[totalFrames * channels];

            for (int b = 0; b < Beats; b++)
            {
                int start = b * beatFrames;

                for (int f = 0; f < decayFrames; f++)
                {
                    float gain = f < fadeStart ? 1f : (float)(decayFrames - f) / fadeFrames;

                    // Wrap: the clip loops while the trigger is held, so without this every repeat has a gap.
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
                       (60f / (loopLength / Beats)).ToString("F0") + " RPM, decay " +
                       decayBeats.ToString("F1") + " beat(s) = " +
                       (decayFrames / (float)freq).ToString("F3") + "s of " +
                       (srcFrames / (float)freq).ToString("F3") + "s sample" +
                       (truncated ? " (cut)" : " (whole)"));

            return loop;
        }
    }
}
