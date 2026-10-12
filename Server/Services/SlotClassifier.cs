using System.Text.RegularExpressions;

namespace ResoundServer.Services;

/// <summary>Guesses a file's slot from its path and name. Every guess is shown and can be changed.</summary>
public static partial class SlotClassifier
{
    public static readonly string[] Slots =
    [
        "body", "tail", "doublet", "body_silenced", "tail_silenced", "doublet_silenced"
    ];

    /// <summary>The events nearly every gun has. Rarer ones arrive through an events/ folder.</summary>
    public static readonly string[] CommonEvents =
    [
        "SndDraw", "SndHolster", "SndMagIn", "SndMagOut", "SndMagPullOut", "SndBoltIn", "SndBoltOut",
        "SndSlideIn", "SndSlideOut", "SndPumpBack", "SndPumpForward", "SndShellIn", "SndDryFire"
    ];

    // First match wins, so the specific phrasings sit above the bare words they contain.
    private static readonly (Regex Pattern, string Event)[] EventNames =
    [
        (new(@"dry.?fire|empty.?click", RegexOptions.IgnoreCase), "SndDryFire"),
        (new(@"(grab|pull).?mag", RegexOptions.IgnoreCase), "SndMagPullOut"),
        (new(@"(insert|in).?mag|mag.?(in|insert)", RegexOptions.IgnoreCase), "SndMagIn"),
        (new(@"(remove|eject|out).?mag|mag.?(out|remove|eject|release)", RegexOptions.IgnoreCase), "SndMagOut"),
        (new(@"bolt.?(back|pull|open|out|rack)|(pull|rack).?bolt|charging.?handle.?back", RegexOptions.IgnoreCase), "SndBoltOut"),
        (new(@"bolt.?(forward|fwd|release|close|in)|charging.?handle.?(forward|release)", RegexOptions.IgnoreCase), "SndBoltIn"),
        (new(@"slide.?(back|pull|out|rack)", RegexOptions.IgnoreCase), "SndSlideOut"),
        (new(@"slide.?(forward|fwd|release|in)", RegexOptions.IgnoreCase), "SndSlideIn"),
        (new(@"pump.?back", RegexOptions.IgnoreCase), "SndPumpBack"),
        (new(@"pump.?(forward|fwd)", RegexOptions.IgnoreCase), "SndPumpForward"),
        (new(@"shell.?(in|insert|load)|load.?shell", RegexOptions.IgnoreCase), "SndShellIn"),
        (new(@"holster", RegexOptions.IgnoreCase), "SndHolster"),
        (new(@"draw|deploy|equip|raise", RegexOptions.IgnoreCase), "SndDraw"),
    ];

    [GeneratedRegex(@"sil(enc)?|suppress|supp_", RegexOptions.IgnoreCase)]
    private static partial Regex Silenced();

    [GeneratedRegex(@"dist|tail|decay|reflect|far", RegexOptions.IgnoreCase)]
    private static partial Regex Tail();

    [GeneratedRegex(@"doublet|burst2|pair", RegexOptions.IgnoreCase)]
    private static partial Regex Doublet();

    // Not filtered out, since one person's junk is another's events/ folder, but flagged.
    [GeneratedRegex(@"foley|dryfire|dry_fire|shell|casing|_lfe|reload|mag_|bolt|slide", RegexOptions.IgnoreCase)]
    private static partial Regex NotAShot();

    [GeneratedRegex(@"_npc|_3p|thirdperson", RegexOptions.IgnoreCase)]
    private static partial Regex ThirdPerson();

    /// <summary>The slot a bare filename suggests.</summary>
    public static string FromName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        // Checked before the fire slots: a handling sound guessed as body plays as a gunshot.
        if (EventFromName(name) is { } handling)
            return "events/" + handling;

        var silenced = Silenced().IsMatch(name);

        if (Tail().IsMatch(name))
            return silenced ? "tail_silenced" : "tail";

        if (Doublet().IsMatch(name))
            return silenced ? "doublet_silenced" : "doublet";

        return silenced ? "body_silenced" : "body";
    }

    /// <summary>The handling event a filename plainly names, or null when it names none.</summary>
    public static string? EventFromName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        foreach (var (pattern, evt) in EventNames)
        {
            if (pattern.IsMatch(name))
                return evt;
        }

        return null;
    }

    /// <summary>Slot folders and events/ folders in the path win over the filename.</summary>
    public static string FromPath(string relativePath)
    {
        var parts = relativePath.Split('/', '\\')
            .Where(p => !string.IsNullOrEmpty(p) && p != ".")
            .ToArray();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (Slots.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                return parts[i].ToLowerInvariant();

            if (parts[i].Equals("events", StringComparison.OrdinalIgnoreCase) && i + 2 <= parts.Length - 1)
                return "events/" + parts[i + 1];
        }

        return FromName(parts.Length > 0 ? parts[^1] : relativePath);
    }

    /// <summary>Why a file might not be a shot at all, or null. Shown, never acted on.</summary>
    public static string? Doubt(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);

        if (NotAShot().IsMatch(name))
            return "this looks like handling rather than the shot. Pick its event from the list, or it plays as a gunshot.";

        if (ThirdPerson().IsMatch(name))
            return "this looks like a third-person take. Resound plays one set to everybody, so " +
                   "the first-person rip is usually the one you want.";

        return null;
    }
}
