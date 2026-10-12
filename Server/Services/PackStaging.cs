using ResoundServer.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;

namespace ResoundServer.Services;

/// <summary>One file on its way into a pack.</summary>
public class StagedCandidate
{
    /// <summary>What the builder will copy. Null when the file cannot be used at all.</summary>
    public StagedFile? File { get; set; }

    /// <summary>The name it arrived under, which is what to call it in a message.</summary>
    public string OriginalName { get; set; } = "";

    public string? Problem { get; set; }
    public string? Fix { get; set; }

    /// <summary>A reason to doubt the slot guess, from the filename. Never acted on by itself.</summary>
    public string? Doubt { get; set; }

    public bool Converted { get; set; }
    public float Seconds { get; set; }
    public int Channels { get; set; }
    public int SampleRate { get; set; }

    public bool Ok => File is not null;

    public string Slot
    {
        get => File?.Slot ?? "body";
        set { if (File is not null) File.Slot = value; }
    }
}

/// <summary>Where audio waits before a build. Playable WAVs are referenced in place; conversions land here.</summary>
[Injectable(InjectionType.Singleton)]
public class PackStaging
{
    private readonly AudioConverter _converter;
    private readonly ISptLogger<PackStaging> _logger;

    public PackStaging(AudioConverter converter, ISptLogger<PackStaging> logger)
    {
        _converter = converter;
        _logger = logger;
    }

    /// <summary>Where converted copies go. Defaults to a folder of our own under the system temp.</summary>
    public string Root { get; set; } =
        Path.Join(Path.GetTempPath(), "resound-staging");

    /// <summary>Every usable file under a folder, recursively, with a slot guessed for each.</summary>
    public List<StagedCandidate> Scan(string folder, string? forceSlot = null)
    {
        if (!Directory.Exists(folder))
        {
            return
            [
                new StagedCandidate
                {
                    OriginalName = folder,
                    Problem = "there is no folder there.",
                    Fix = "Paste the full path, the way Explorer shows it in the address bar."
                }
            ];
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        }
        catch (Exception e)
        {
            return [new StagedCandidate { OriginalName = folder, Problem = $"the folder could not be read: {e.Message}" }];
        }

        var candidates = new List<StagedCandidate>();

        foreach (var file in files.Where(AudioConverter.LooksLikeAudio).OrderBy(f => f))
        {
            var relative = Path.GetRelativePath(folder, file);
            candidates.Add(Take(file, forceSlot ?? SlotClassifier.FromPath(relative)));
        }

        if (candidates.Count == 0)
        {
            candidates.Add(new StagedCandidate
            {
                OriginalName = folder,
                Problem = "there is no audio in that folder or anything under it.",
                Fix = "Resound reads " + string.Join(", ", AudioConverter.KnownAudioExtensions) + "."
            });
        }

        return candidates;
    }

    /// <summary>Takes a file already on disk, converting it into the staging folder if it has to.</summary>
    public StagedCandidate Take(string path, string? slot = null)
    {
        var name = Path.GetFileName(path);

        var candidate = new StagedCandidate
        {
            OriginalName = name
        };

        slot ??= SlotClassifier.FromName(name);

        // Already sorted into an event, so the "might be handling" warning would only be noise.
        if (!slot.StartsWith("events/", StringComparison.OrdinalIgnoreCase))
            candidate.Doubt = SlotClassifier.Doubt(name);

        // Already playable: reference it where it lies rather than making a copy nobody asked for.
        if (Path.GetExtension(path).Equals(AudioConverter.PlayableExtension, StringComparison.OrdinalIgnoreCase))
        {
            var info = WavInspector.Inspect(path);

            if (info.Ok)
            {
                Describe(candidate, info);
                candidate.File = new StagedFile { Slot = slot, SourcePath = path };
                return candidate;
            }
        }

        var destination = Path.Join(Root, Guid.NewGuid().ToString("N"),
            Path.GetFileNameWithoutExtension(name) + AudioConverter.PlayableExtension);

        var result = _converter.Prepare(path, destination);

        if (!result.Ok)
        {
            candidate.Problem = result.Problem;
            candidate.Fix = result.Fix;
            return candidate;
        }

        candidate.Converted = result.Converted;
        Describe(candidate, WavInspector.Inspect(destination));

        // Keeps the name someone recognises rather than the guid the copy lives under.
        candidate.File = new StagedFile
        {
            Slot = slot,
            SourcePath = destination,
            FileName = Path.GetFileNameWithoutExtension(name) + AudioConverter.PlayableExtension
        };

        return candidate;
    }

    /// <summary>Takes an upload, which has to be written down before anything can look at it.</summary>
    public async Task<StagedCandidate> TakeAsync(Stream contents, string fileName, string? slot = null,
                                                 CancellationToken cancellationToken = default)
    {
        var landing = Path.Join(Root, "incoming", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(landing);

        var path = Path.Join(landing, Path.GetFileName(fileName));

        try
        {
            await using (var file = File.Create(path))
                await contents.CopyToAsync(file, cancellationToken);
        }
        catch (Exception e)
        {
            return new StagedCandidate { OriginalName = fileName, Problem = $"the upload failed: {e.Message}" };
        }

        var candidate = Take(path, slot);

        // The landing copy is only worth keeping when it IS the staged file.
        if (candidate.File?.SourcePath != path)
            Discard(landing);

        return candidate;
    }

    /// <summary>The bytes of a staged file, for playing it back in the browser before it ships.</summary>
    public byte[]? Read(StagedFile file, long maxBytes = 12L * 1024 * 1024)
    {
        try
        {
            var info = new FileInfo(file.SourcePath);

            if (!info.Exists || info.Length > maxBytes)
                return null;

            return File.ReadAllBytes(file.SourcePath);
        }
        catch (Exception e)
        {
            _logger.Warning($"[Resound] Could not read staged file {file.SourcePath}: {e.Message}");
            return null;
        }
    }

    /// <summary>Deletes a converted copy. Never touches files referenced in place.</summary>
    public void Discard(StagedFile? file)
    {
        if (file is null || !IsOurs(file.SourcePath))
            return;

        Discard(Path.GetDirectoryName(file.SourcePath));
    }

    private void Discard(string? directory)
    {
        if (directory is null || !IsOurs(directory))
            return;

        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception e)
        {
            _logger.Warning($"[Resound] Could not clear staging folder {directory}: {e.Message}");
        }
    }

    /// <summary>Everything staged this session. Deletes the root directly, which Discard refuses to do.</summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch (Exception e)
        {
            _logger.Warning($"[Resound] Could not clear the staging folder: {e.Message}");
        }
    }

    private bool IsOurs(string path)
    {
        var root = Path.GetFullPath(Root);
        var full = Path.GetFullPath(path);

        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length;
    }

    private static void Describe(StagedCandidate candidate, WavInfo info)
    {
        candidate.Seconds = info.Duration;
        candidate.Channels = info.Channels;
        candidate.SampleRate = info.SampleRate;
    }
}
