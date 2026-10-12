using System;
using System.IO;
using UnityEngine;

namespace Resound.Audio
{
    // Header facts only: the pack report runs before Unity can build a working AudioClip.
    internal class WavInfo
    {
        public bool Ok;
        public string Problem;   // null when Ok, a sentence aimed at a pack author otherwise
        public string Note;      // Ok but worth saying, e.g. a stereo file that will be folded
        public int Channels;
        public int SampleRate;
        public int Bits;
        public int Format;
        public float Duration;
    }

    // Decoded by hand because UnityWebRequestMultimedia is async.
    internal static class WavLoader
    {
        private const int PCM = 1, FLOAT = 3, EXTENSIBLE = 0xFFFE;

        // Header only, so unlike Load it is safe during Awake.
        public static WavInfo Inspect(string path)
        {
            WavInfo info = new WavInfo();

            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (BinaryReader r = new BinaryReader(fs))
                {
                    if (fs.Length < 12 || Tag(r) != "RIFF")
                    {
                        info.Problem = "not a WAV file (no RIFF header). Convert it to WAV.";
                        return info;
                    }

                    r.ReadInt32();

                    if (Tag(r) != "WAVE")
                    {
                        info.Problem = "not a WAV file. Convert it to WAV.";
                        return info;
                    }

                    long dataLength = -1;

                    while (fs.Position + 8 <= fs.Length)
                    {
                        string id = Tag(r);
                        long size = (uint)r.ReadInt32();
                        long body = fs.Position;

                        if (id == "fmt " && size >= 16)
                        {
                            info.Format = r.ReadInt16();
                            info.Channels = r.ReadInt16();
                            info.SampleRate = r.ReadInt32();
                            r.ReadInt32();  // byte rate
                            r.ReadInt16();  // block align
                            info.Bits = r.ReadInt16();

                            // WAVE_FORMAT_EXTENSIBLE hides the real format in the GUID.
                            if (info.Format == EXTENSIBLE && size >= 26)
                            {
                                fs.Position = body + 24;
                                info.Format = r.ReadInt16();
                            }
                        }
                        else if (id == "data")
                        {
                            dataLength = Math.Min(size, fs.Length - body);
                        }

                        fs.Position = body + size + (size & 1); // chunks are word-aligned
                    }

                    if (info.Channels <= 0 || info.SampleRate <= 0 || dataLength < 0)
                    {
                        info.Problem = "the WAV header is malformed or truncated. Re-export it.";
                        return info;
                    }

                    if (info.Format != PCM && !(info.Format == FLOAT && info.Bits == 32))
                    {
                        info.Problem = "compressed WAV (format " + info.Format + "). Re-export as PCM " +
                                       "16-bit; ADPCM and mp3-in-a-wav cannot be read.";
                        return info;
                    }

                    if (info.Format == PCM && info.Bits != 8 && info.Bits != 16 &&
                        info.Bits != 24 && info.Bits != 32)
                    {
                        info.Problem = info.Bits + "-bit audio is not supported. Re-export as PCM 16-bit.";
                        return info;
                    }

                    int frameBytes = info.Channels * info.Bits / 8;
                    if (frameBytes > 0)
                        info.Duration = dataLength / (float)frameBytes / info.SampleRate;

                    if (info.Duration <= 0f)
                    {
                        info.Problem = "there is no audio in the file (zero length).";
                        return info;
                    }

                    if (info.Channels > 1)
                        info.Note = info.Channels + " channels, folded to mono on load " +
                                    "(Unity cannot place a stereo clip in 3D space)";

                    info.Ok = true;
                    return info;
                }
            }
            catch (Exception e)
            {
                info.Problem = "could not be read: " + e.Message;
                return info;
            }
        }

        public static AudioClip Load(string path)
        {
            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read " + path + ": " + e.Message);
                return null;
            }

            if (raw.Length < 12 || Tag(raw, 0) != "RIFF" || Tag(raw, 8) != "WAVE")
            {
                Plugin.Log.LogWarning("Not a RIFF/WAVE file: " + path);
                return null;
            }

            int channels = 0, sampleRate = 0, bitsPerSample = 0, format = 0;
            int dataOffset = -1, dataLength = 0;

            // Walk the chunks; a LIST before data means fmt/data are not at fixed offsets.
            int pos = 12;
            while (pos + 8 <= raw.Length)
            {
                string id = Tag(raw, pos);
                int size = BitConverter.ToInt32(raw, pos + 4);
                int body = pos + 8;

                if (size < 0 || body + size > raw.Length)
                    size = raw.Length - body; // tolerate a truncated final chunk

                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(raw, body);
                    channels = BitConverter.ToInt16(raw, body + 2);
                    sampleRate = BitConverter.ToInt32(raw, body + 4);
                    bitsPerSample = BitConverter.ToInt16(raw, body + 14);

                    if (format == EXTENSIBLE && size >= 26)
                        format = BitConverter.ToInt16(raw, body + 24);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = size;
                }

                pos = body + size + (size & 1);
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            {
                Plugin.Log.LogWarning("Malformed WAVE header: " + path);
                return null;
            }

            float[] samples = Decode(raw, dataOffset, dataLength, format, bitsPerSample, path);
            if (samples == null || samples.Length == 0)
                return null;

            // Unity will not spatialize a multi-channel clip, so stereo is folded rather than rejected.
            if (channels > 1)
            {
                samples = ToMono(samples, channels);
                Plugin.Dbg("Folded " + channels + " channels to mono: " + Path.GetFileName(path));
                channels = 1;
            }

            string name = Path.GetFileNameWithoutExtension(path);
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Averaged, not summed, so a correlated stereo pair keeps its level instead of doubling.
        private static float[] ToMono(float[] interleaved, int channels)
        {
            int frames = interleaved.Length / channels;
            float[] mono = new float[frames];

            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                int b = f * channels;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[b + c];

                mono[f] = sum / channels;
            }

            return mono;
        }

        private static float[] Decode(byte[] raw, int offset, int length, int format, int bits, string path)
        {
            if (format == FLOAT && bits == 32)
            {
                float[] outp = new float[length / 4];
                for (int i = 0; i < outp.Length; i++)
                    outp[i] = BitConverter.ToSingle(raw, offset + i * 4);
                return outp;
            }

            if (format != PCM)
            {
                Plugin.Log.LogWarning("Unsupported WAVE format " + format + " (need PCM or float32): " + path +
                                      "; re-export as PCM WAV.");
                return null;
            }

            switch (bits)
            {
                case 8:
                {
                    // 8-bit PCM is unsigned, centred on 128.
                    float[] outp = new float[length];
                    for (int i = 0; i < outp.Length; i++)
                        outp[i] = (raw[offset + i] - 128) / 128f;
                    return outp;
                }
                case 16:
                {
                    float[] outp = new float[length / 2];
                    for (int i = 0; i < outp.Length; i++)
                        outp[i] = BitConverter.ToInt16(raw, offset + i * 2) / 32768f;
                    return outp;
                }
                case 24:
                {
                    float[] outp = new float[length / 3];
                    for (int i = 0; i < outp.Length; i++)
                    {
                        int b = offset + i * 3;
                        int v = (raw[b] << 8) | (raw[b + 1] << 16) | (raw[b + 2] << 24); // sign-extend via the top byte
                        outp[i] = (v >> 8) / 8388608f;
                    }
                    return outp;
                }
                case 32:
                {
                    float[] outp = new float[length / 4];
                    for (int i = 0; i < outp.Length; i++)
                        outp[i] = BitConverter.ToInt32(raw, offset + i * 4) / 2147483648f;
                    return outp;
                }
                default:
                    Plugin.Log.LogWarning("Unsupported bit depth " + bits + ": " + path);
                    return null;
            }
        }

        private static string Tag(byte[] b, int at)
        {
            return "" + (char)b[at] + (char)b[at + 1] + (char)b[at + 2] + (char)b[at + 3];
        }

        private static string Tag(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            return b.Length < 4 ? "" : "" + (char)b[0] + (char)b[1] + (char)b[2] + (char)b[3];
        }
    }
}
