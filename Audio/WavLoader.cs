using System;
using System.IO;
using UnityEngine;

namespace Resound.Audio
{
    // Minimal RIFF reader, PCM 8/16/24/32 and float32. Decoded here rather than via
    // UnityWebRequestMultimedia because that path is async.
    internal static class WavLoader
    {
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

                    // WAVE_FORMAT_EXTENSIBLE stores the real format in the GUID's first two bytes.
                    if (format == 0xFFFE && size >= 26)
                        format = BitConverter.ToInt16(raw, body + 24);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = size;
                }

                pos = body + size + (size & 1); // chunks are word-aligned
            }

            if (dataOffset < 0 || channels <= 0 || sampleRate <= 0)
            {
                Plugin.Log.LogWarning("Malformed WAVE header: " + path);
                return null;
            }

            float[] samples = Decode(raw, dataOffset, dataLength, format, bitsPerSample, path);
            if (samples == null || samples.Length == 0)
                return null;

            string name = Path.GetFileNameWithoutExtension(path);
            AudioClip clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float[] Decode(byte[] raw, int offset, int length, int format, int bits, string path)
        {
            const int PCM = 1, FLOAT = 3;

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
    }
}

