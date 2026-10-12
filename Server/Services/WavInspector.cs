namespace ResoundServer.Services;

/// <summary>Port of the plugin's WavLoader.Inspect. Keep the two identical.</summary>
public class WavInfo
{
    public bool Ok { get; set; }

    /// <summary>Null when Ok, otherwise a sentence aimed at whoever is making the pack.</summary>
    public string? Problem { get; set; }

    /// <summary>Loads fine but worth saying, e.g. a stereo file that will be folded.</summary>
    public string? Note { get; set; }

    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public int Bits { get; set; }
    public int Format { get; set; }
    public float Duration { get; set; }
}

public static class WavInspector
{
    private const int Pcm = 1, Float = 3, Extensible = 0xFFFE;

    /// <summary>Header only, cheap enough for every page load.</summary>
    public static WavInfo Inspect(string path)
    {
        var info = new WavInfo();

        try
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

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
                var id = Tag(r);
                long size = (uint)r.ReadInt32();
                var body = fs.Position;

                if (id == "fmt " && size >= 16)
                {
                    info.Format = r.ReadInt16();
                    info.Channels = r.ReadInt16();
                    info.SampleRate = r.ReadInt32();
                    r.ReadInt32(); // byte rate
                    r.ReadInt16(); // block align
                    info.Bits = r.ReadInt16();

                    // WAVE_FORMAT_EXTENSIBLE hides the real format in the GUID.
                    if (info.Format == Extensible && size >= 26)
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

            if (info.Format != Pcm && !(info.Format == Float && info.Bits == 32))
            {
                info.Problem = $"compressed WAV (format {info.Format}). Re-export as PCM 16-bit; " +
                               "ADPCM and mp3-in-a-wav cannot be read.";
                return info;
            }

            if (info.Format == Pcm && info.Bits is not (8 or 16 or 24 or 32))
            {
                info.Problem = $"{info.Bits}-bit audio is not supported. Re-export as PCM 16-bit.";
                return info;
            }

            var frameBytes = info.Channels * info.Bits / 8;
            if (frameBytes > 0)
                info.Duration = dataLength / (float)frameBytes / info.SampleRate;

            if (info.Duration <= 0f)
            {
                info.Problem = "there is no audio in the file (zero length).";
                return info;
            }

            if (info.Channels > 1)
                info.Note = $"{info.Channels} channels, folded to mono on load " +
                            "(Unity cannot place a stereo clip in 3D space)";

            info.Ok = true;
            return info;
        }
        catch (Exception e)
        {
            info.Problem = $"could not be read: {e.Message}";
            return info;
        }
    }

    private static string Tag(BinaryReader r)
    {
        return System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
    }
}
