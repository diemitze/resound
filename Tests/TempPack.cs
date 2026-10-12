namespace ResoundServer.Tests;

/// <summary>A throwaway pack folder with real WAV headers, since the checks read headers, not names.</summary>
public sealed class TempPack : IDisposable
{
    public string Root { get; }

    public TempPack()
    {
        Root = Path.Join(Path.GetTempPath(), "resound-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>A path inside this folder. Named At so it does not shadow System.IO.Path.</summary>
    public string At(params string[] parts) => Path.Join([Root, .. parts]);

    /// <summary>A playable WAV, unless you ask for a shape the loader rejects.</summary>
    public string Wav(string relativePath, int channels = 1, int bits = 16, int sampleRate = 48000,
                      double seconds = 0.4, int format = 1)
    {
        var path = At(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var bytesPerFrame = channels * bits / 8;
        var dataLength = (int)(sampleRate * seconds) * bytesPerFrame;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataLength);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)format);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * bytesPerFrame);
        w.Write((short)bytesPerFrame);
        w.Write((short)bits);
        w.Write("data"u8.ToArray());
        w.Write(dataLength);
        w.Write(new byte[dataLength]);

        return path;
    }

    /// <summary>Something that is not audio, whatever its extension claims.</summary>
    public string File_(string relativePath, string contents = "not audio")
    {
        var path = At(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    public string Dir(string relativePath)
    {
        var path = At(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch
        {
            // A locked file in a temp folder is not worth failing a passing test over.
        }
    }
}
