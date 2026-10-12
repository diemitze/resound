namespace ResoundServer.Services;

/// <summary>Readable class and calibre names. Display only.</summary>
public static class Labels
{
    private static readonly Dictionary<string, string> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assaultCarbine"] = "Assault carbine",
        ["assaultRifle"] = "Assault rifle",
        ["grenadeLauncher"] = "Grenade launcher",
        ["machinegun"] = "Machine gun",
        ["marksmanRifle"] = "Marksman rifle",
        ["pistol"] = "Pistol",
        ["shotgun"] = "Shotgun",
        ["smg"] = "SMG",
        ["sniperRifle"] = "Sniper rifle",
        ["specialWeapon"] = "Special weapon"
    };

    // Where the name on the box is not the measurement, the name wins.
    private static readonly Dictionary<string, string> Calibres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1143x23ACP"] = ".45 ACP",
        ["127x33"] = ".50 AE",
        ["127x99"] = ".50 BMG",
        ["762x35"] = ".300 BLK",
        ["86x70"] = ".338 LM",
        ["366TKM"] = ".366 TKM",
        ["12g"] = "12ga",
        ["20g"] = "20ga",
        ["40mmRU"] = "40mm VOG",
        ["40x46"] = "40x46mm",
        ["20x1mm"] = "20x1mm",
        ["30x29"] = "30x29mm",
        ["26x75"] = "26x75mm",
        ["23x75"] = "23x75mm"
    };

    public static string Class(string? weapClass)
    {
        if (string.IsNullOrEmpty(weapClass))
            return "-";

        return Classes.TryGetValue(weapClass, out var label) ? label : weapClass;
    }

    /// <summary>"Caliber762x39" as 7.62x39, "Caliber556x45NATO" as 5.56x45.</summary>
    public static string Calibre(string? caliber)
    {
        if (string.IsNullOrEmpty(caliber))
            return "-";

        var raw = caliber.StartsWith("Caliber", StringComparison.OrdinalIgnoreCase) ? caliber[7..] : caliber;

        if (Calibres.TryGetValue(raw, out var named))
            return named;

        var x = raw.IndexOf('x');
        if (x <= 0)
            return raw;

        var bore = raw[..x];
        var rest = raw[(x + 1)..];

        // The suffixes are BSG's disambiguators, not part of how anyone says the round.
        foreach (var tag in new[] { "NATO", "PARA", "PMM", "PM", "TT" })
        {
            if (rest.EndsWith(tag, StringComparison.Ordinal))
            {
                rest = rest[..^tag.Length];
                break;
            }
        }

        // Millimetres written without the point: 762 is 7.62, 127 is 12.7, 46 is 4.6.
        if (bore.All(char.IsDigit) && bore.Length >= 2)
            bore = bore.StartsWith('1') && bore.Length == 3 ? bore[..2] + "." + bore[2..] : bore[..1] + "." + bore[1..];

        return bore + "x" + rest;
    }
}
