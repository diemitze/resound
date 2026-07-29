using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ResoundServer.Models;

// What the client asks for over /resound/config. Deliberately small: the client already knows
// how to load packs, this only tells it which one to prefer.
public class ResoundWebConfig
{
    /// <summary>"Auto" lets every installed pack compete on priority.</summary>
    [JsonPropertyName("activePack")]
    public string ActivePack { get; set; } = "Auto";

    /// <summary>Weapon template id to pack id. Beats ActivePack and every priority.</summary>
    [JsonPropertyName("overrides")]
    public Dictionary<string, string> Overrides { get; set; } = new();
}

// One row in the weapons table. Built from the server's item database, which is the only place
// a full weapon list with real names exists.
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
    public List<PackRule> Rules { get; set; } = new();

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

    // Mirrors the client's weights exactly. Changing one without the other makes the page lie.
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

// One line of the decision table the client asks for. Deliberately carries the pack id and set
// name rather than a path: with Fika the client can be on another machine entirely, where the
// server's absolute paths mean nothing. The server decides, the client locates.
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
}
