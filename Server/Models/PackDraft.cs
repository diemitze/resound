namespace ResoundServer.Models;

/// <summary>A pack before it exists on disk, shaped like the manifest it becomes.</summary>
public class PackDraft
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public int Priority { get; set; }
    public bool AutoLevel { get; set; } = true;

    public List<SetDraft> Sets { get; set; } = new();
}

/// <summary>One entry plus its audio. Holds a real PackRule so the claims preview uses the real matcher.</summary>
public class SetDraft
{
    public PackRule Rule { get; set; } = new();

    /// <summary>Null follows the pack. PackRule.AutoLevel cannot say that once a pack is read back.</summary>
    public bool? AutoLevel { get; set; }

    public List<StagedFile> Files { get; set; } = new();

    public string SoundSet
    {
        get => Rule.SoundSet;
        set => Rule.SoundSet = value;
    }
}

/// <summary>A file waiting to go into a slot, from a scanned folder or an upload.</summary>
public class StagedFile
{
    /// <summary>One of the six slots, or "events/SndMagOut".</summary>
    public string Slot { get; set; } = "body";

    public string SourcePath { get; set; } = "";

    /// <summary>What to call it in the pack. Falls back to the source file's own name.</summary>
    public string? FileName { get; set; }

    public string TargetName =>
        string.IsNullOrWhiteSpace(FileName) ? Path.GetFileName(SourcePath) : FileName;
}

/// <summary>What happened when a draft was written.</summary>
public class BuildResult
{
    public bool Written { get; set; }
    public string Directory { get; set; } = "";
    public int FilesCopied { get; set; }

    /// <summary>Why nothing was written. Empty when <see cref="Written"/> is true.</summary>
    public List<PackProblem> Refusals { get; set; } = new();

    /// <summary>The pack checked after writing, so the builder closes its own loop.</summary>
    public PackHealth? Health { get; set; }
}
