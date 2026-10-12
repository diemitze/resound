namespace ResoundServer.Models;

/// <summary>One problem or note about a pack, structured so the page can show it beside its cause.</summary>
public class PackProblem
{
    /// <summary>A note loads fine; a problem is why something still plays its vanilla sound.</summary>
    public bool IsNote { get; set; }

    /// <summary>The entry this belongs to, or null when it is about the whole pack.</summary>
    public string? Entry { get; set; }

    /// <summary>Where inside the set: "body", "events/SndMagIn", "body/shot.wav".</summary>
    public string? Where { get; set; }

    public string Message { get; set; } = "";

    /// <summary>Optional second line: the command or edit that fixes it.</summary>
    public string? Fix { get; set; }
}

public class SlotHealth
{
    public string Slot { get; set; } = "";

    /// <summary>Files that will actually load.</summary>
    public int Clips { get; set; }

    /// <summary>Files sitting in the slot that will not.</summary>
    public int Bad { get; set; }
}

public class SetHealth
{
    public string Name { get; set; } = "";
    public string EntryName { get; set; } = "";
    public bool Exists { get; set; }
    public bool Enabled { get; set; } = true;
    public List<SlotHealth> Slots { get; set; } = new();

    public int Clips => Slots.Sum(s => s.Clips);
}

public class PackHealth
{
    public string PackId { get; set; } = "";
    public string PackName { get; set; } = "";
    public string Directory { get; set; } = "";
    public List<SetHealth> Sets { get; set; } = new();
    public List<PackProblem> Items { get; set; } = new();

    public IEnumerable<PackProblem> Problems => Items.Where(i => !i.IsNote);
    public IEnumerable<PackProblem> Notes => Items.Where(i => i.IsNote);

    public int ProblemCount => Items.Count(i => !i.IsNote);
    public int Clips => Sets.Sum(s => s.Clips);
    public bool Healthy => ProblemCount == 0;
}
