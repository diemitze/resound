using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResoundServer.Models;

// What the client reads from /resound/config: which pack to prefer, and the pins.
public class ResoundWebConfig
{
    /// <summary>"Auto" lets every installed pack compete on priority.</summary>
    [JsonPropertyName("activePack")]
    public string ActivePack { get; set; } = "Auto";

    /// <summary>Weapon template id to pack id. Beats ActivePack and every priority.</summary>
    [JsonPropertyName("overrides")]
    public Dictionary<string, string> Overrides { get; set; } = new();
}

// One row in the weapons table, from the server's item database.
public class WeaponRow
{
    public string Id { get; set; } = "";
    public string ParentId { get; set; } = "";
    public string InternalName { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string WeapClass { get; set; } = "";
    public string Caliber { get; set; } = "";
}

// A pack found in the client's plugins folder.
public class PackRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public int Priority { get; set; }
    public bool AutoLevel { get; set; } = true;
    public List<PackRule> Rules { get; set; } = new();

    /// <summary>Server only. The client gets ids and set names, which a Fika peer can resolve locally.</summary>
    public string Directory { get; set; } = "";

    /// <summary>The built-ins too: their sets are under Resound\sounds\ like any pack's.</summary>
    public string SoundsRoot => string.IsNullOrEmpty(Directory) ? "" : Path.Join(Directory, "sounds");

    public int Entries => Rules.Count;
    public string Display => string.IsNullOrEmpty(Name) ? Id : Name;
}

// One entry's match block, enough to work out which weapons a pack claims.
public class PackRule
{
    public string Name { get; set; } = "";
    public string SoundSet { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public string? TemplateId { get; set; }
    public List<string>? TemplateIds { get; set; }
    public string? ParentId { get; set; }
    public string? WeapClass { get; set; }
    public string? Caliber { get; set; }
    public string? NamePattern { get; set; }

    public float BodyVolume { get; set; } = 1f;
    public float TailVolume { get; set; } = 1f;
    public float? BodySilencedVolume { get; set; }
    public float? TailSilencedVolume { get; set; }
    public float EventVolume { get; set; } = 1f;
    public float? DecayBeats { get; set; }

    // Pack default unless the entry overrides it; mirrors the client.
    public bool AutoLevel { get; set; } = true;

    // Must match the client's weights, or the page lies.
    public int Specificity
    {
        get
        {
            var s = 0;
            if (!string.IsNullOrEmpty(TemplateId)) s += 100;
            if (TemplateIds is { Count: > 0 }) s += 90;
            if (!string.IsNullOrEmpty(ParentId)) s += 50;
            if (!string.IsNullOrEmpty(WeapClass)) s += 40;
            if (!string.IsNullOrEmpty(Caliber)) s += 30;
            if (!string.IsNullOrEmpty(NamePattern)) s += 20;
            return s;
        }
    }

    public bool Matches(WeaponRow w)
    {
        if (!string.IsNullOrEmpty(TemplateId) && !TemplateId.Equals(w.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        if (TemplateIds is { Count: > 0 } &&
            !TemplateIds.Any(t => t.Equals(w.Id, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrEmpty(ParentId) && !ParentId.Equals(w.ParentId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(WeapClass) && !WeapClass.Equals(w.WeapClass, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(Caliber) && !Caliber.Equals(w.Caliber, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(NamePattern))
        {
            try
            {
                if (!Regex.IsMatch(w.InternalName, NamePattern, RegexOptions.IgnoreCase))
                    return false;
            }
            catch
            {
                // A malformed pattern claims nothing rather than throwing on every row.
                return false;
            }
        }

        return true;
    }
}

public enum SoundSource
{
    Vanilla,
    Builtin,
    Addon
}

// What a weapon actually sounds like right now, and why.
public class Effective
{
    public SoundSource Source { get; set; } = SoundSource.Vanilla;
    public string PackName { get; set; } = "";
    public string EntryName { get; set; } = "";

    public string Label => Source switch
    {
        SoundSource.Vanilla => "Vanilla",
        SoundSource.Builtin => "Resound",
        _ => PackName
    };
}

// Pack id and set name, not a path: under Fika the client may be another machine.
public class ResolvedSound
{
    [JsonPropertyName("packId")] public string PackId { get; set; } = "";
    [JsonPropertyName("soundSet")] public string SoundSet { get; set; } = "";
    [JsonPropertyName("entryName")] public string EntryName { get; set; } = "";
    [JsonPropertyName("bodyVolume")] public float BodyVolume { get; set; } = 1f;
    [JsonPropertyName("tailVolume")] public float TailVolume { get; set; } = 1f;
    [JsonPropertyName("bodySilencedVolume")] public float? BodySilencedVolume { get; set; }
    [JsonPropertyName("tailSilencedVolume")] public float? TailSilencedVolume { get; set; }
    [JsonPropertyName("eventVolume")] public float EventVolume { get; set; } = 1f;
    [JsonPropertyName("decayBeats")] public float? DecayBeats { get; set; }

    // Whether the client should level-match this set against the vanilla sound it replaces.
    [JsonPropertyName("autoLevel")] public bool AutoLevel { get; set; } = true;
}
