using System.Diagnostics;
using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;

namespace ResoundServer.Services;

public class ConversionResult
{
    public bool Ok { get; set; }
    public string? Problem { get; set; }
    public string? Fix { get; set; }

    /// <summary>True when the file was re-encoded rather than copied as it was.</summary>
    public bool Converted { get; set; }
}

/// <summary>Makes a dropped file loadable: playable WAVs are copied as-is, anything else needs ffmpeg.</summary>
[Injectable(InjectionType.Singleton)]
public class AudioConverter
{
    /// <summary>What the plugin will try to load. Everything else has to be converted first.</summary>
    public const string PlayableExtension = ".wav";

    /// <summary>Extensions picked up from a folder; readmes and logs beside the audio are ignored.</summary>
    public static readonly string[] KnownAudioExtensions =
    [
        ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".opus", ".wv"
    ];

    public static bool LooksLikeAudio(string path)
    {
        return KnownAudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private readonly ISptLogger<AudioConverter> _logger;
    private string? _ffmpeg;
    private bool _looked;

    public AudioConverter(ISptLogger<AudioConverter> logger)
    {
        _logger = logger;
    }

    /// <summary>Set to point at a particular ffmpeg, or to an empty string to act as if it is absent.</summary>
    public string? FfmpegOverride { get; set; }

    public string? FfmpegPath
    {
        get
        {
            if (FfmpegOverride is not null)
                return string.IsNullOrEmpty(FfmpegOverride) ? null : FfmpegOverride;

            if (_looked)
                return _ffmpeg;

            _looked = true;
            _ffmpeg = FindFfmpeg();

            if (_ffmpeg is null)
                _logger.Info("[Resound] ffmpeg not found; the pack builder will only accept WAV files.");

            return _ffmpeg;
        }
    }

    public bool HasFfmpeg => FfmpegPath is not null;

    /// <summary>Whether a file can be taken at all, without touching it.</summary>
    public bool CanAccept(string path)
    {
        return IsWav(path) ? WavInspector.Inspect(path).Ok : HasFfmpeg;
    }

    /// <summary>Puts a playable .wav copy at destination. Writes nothing unless the result is Ok.</summary>
    public ConversionResult Prepare(string source, string destination)
    {
        if (!File.Exists(source))
            return Fail($"'{Path.GetFileName(source)}' is not there any more.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (IsWav(source))
        {
            var info = WavInspector.Inspect(source);

            if (!info.Ok)
            {
                // A WAV ffmpeg can re-encode into one the plugin reads, e.g. ADPCM.
                return HasFfmpeg
                    ? Convert(source, destination)
                    : Fail($"{Path.GetFileName(source)}: {info.Problem}", FfmpegCommand(source));
            }

            File.Copy(source, destination, true);
            return new ConversionResult { Ok = true };
        }

        if (!HasFfmpeg)
        {
            return Fail(
                $"'{Path.GetFileName(source)}' is not a WAV, and Resound only reads WAV.",
                "Install ffmpeg (winget install Gyan.FFmpeg) and this page will convert it for " +
                "you, or convert it yourself: " + FfmpegCommand(source));
        }

        return Convert(source, destination);
    }

    /// <summary>Mono so Unity can spatialize it, 48kHz PCM16 because that is what the game runs at.</summary>
    private ConversionResult Convert(string source, string destination)
    {
        var start = new ProcessStartInfo
        {
            FileName = FfmpegPath!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y", "-i", source,
                     "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le", destination
                 })
            start.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(start);

            if (process is null)
                return Fail("ffmpeg would not start.");

            var errors = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(true); } catch { /* already gone */ }
                return Fail($"ffmpeg gave up on '{Path.GetFileName(source)}'.");
            }

            if (process.ExitCode != 0 || !File.Exists(destination))
            {
                _logger.Warning($"[Resound] ffmpeg failed on {source}: {errors}");
                return Fail($"'{Path.GetFileName(source)}' could not be converted.",
                    errors.Split('\n').FirstOrDefault()?.Trim());
            }

            // Converted and still unreadable means the source was not really audio.
            var info = WavInspector.Inspect(destination);

            if (!info.Ok)
            {
                File.Delete(destination);
                return Fail($"'{Path.GetFileName(source)}' converted to something unplayable: {info.Problem}");
            }

            return new ConversionResult { Ok = true, Converted = true };
        }
        catch (Exception e)
        {
            return Fail($"'{Path.GetFileName(source)}' could not be converted: {e.Message}");
        }
    }

    private static bool IsWav(string path)
    {
        return Path.GetExtension(path).Equals(PlayableExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static string FfmpegCommand(string source)
    {
        var name = Path.GetFileName(source);
        return $"ffmpeg -i \"{name}\" -ac 1 -ar 48000 -c:a pcm_s16le \"{Path.GetFileNameWithoutExtension(name)}.wav\"";
    }

    private static ConversionResult Fail(string problem, string? fix = null)
    {
        return new ConversionResult { Problem = problem, Fix = fix };
    }

    /// <summary>The same places make_pack.ps1 looks, so a machine set up for that one works here.</summary>
    private static string? FindFfmpeg()
    {
        var candidates = new List<string>();

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        candidates.AddRange(path.Split(Path.PathSeparator)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.Join(d.Trim(), exe)));

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(@"C:\Program Files\ShareX\ffmpeg.exe");
            candidates.Add(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
            candidates.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
        }
        else
        {
            candidates.Add("/usr/bin/ffmpeg");
            candidates.Add("/usr/local/bin/ffmpeg");
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // A malformed PATH entry is not worth failing over.
            }
        }

        return null;
    }
}
