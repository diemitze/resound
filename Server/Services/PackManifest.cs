using System.Text.Json;
using ResoundServer.Models;

namespace ResoundServer.Services;

/// <summary>Reads resound.pack.json. Shared with the builder so whatever it writes reads back unchanged.</summary>
public static class PackManifest
{
    public const string FileName = "resound.pack.json";

    /// <summary>Throws on malformed JSON so the caller can name the file.</summary>
    public static PackRow Read(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        var dir = Path.GetDirectoryName(manifestPath) ?? "";

        // Level-matching is on unless the pack opts out, matching the client.
        var autoLevel = !root.TryGetProperty("autoLevel", out var alv) || alv.ValueKind != JsonValueKind.False;

        return new PackRow
        {
            Id = GetString(root, "id") ?? Path.GetFileName(dir) ?? "unknown",
            Directory = dir,
            Name = GetString(root, "name") ?? "",
            Author = GetString(root, "author") ?? "",
            Priority = root.TryGetProperty("priority", out var p) && p.TryGetInt32(out var pv) ? pv : 0,
            AutoLevel = autoLevel,
            Rules = ReadRules(root, autoLevel)
        };
    }

    public static List<PackRule> ReadRules(JsonElement root, bool packAutoLevel)
    {
        var rules = new List<PackRule>();

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return rules;

        foreach (var e in entries.EnumerateArray())
        {
            var rule = new PackRule
            {
                Name = GetString(e, "name") ?? "",
                SoundSet = GetString(e, "soundSet") ?? "",
                Enabled = !e.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False,
                BodyVolume = GetFloat(e, "bodyVolume") ?? 1f,
                TailVolume = GetFloat(e, "tailVolume") ?? 1f,
                BodySilencedVolume = GetFloat(e, "bodySilencedVolume"),
                TailSilencedVolume = GetFloat(e, "tailSilencedVolume"),
                EventVolume = GetFloat(e, "eventVolume") ?? 1f,
                DecayBeats = GetFloat(e, "decayBeats"),
                AutoLevel = e.TryGetProperty("autoLevel", out var al) && al.ValueKind != JsonValueKind.Null
                    ? al.ValueKind == JsonValueKind.True
                    : packAutoLevel
            };

            if (e.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object)
                ApplyMatch(m, rule);

            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>Packs keep match fields under "match", mapping.json on the entry itself; both readers use this.</summary>
    public static void ApplyMatch(JsonElement m, PackRule rule)
    {
        rule.TemplateId = GetString(m, "templateId");
        rule.ParentId = GetString(m, "parentId");
        rule.WeapClass = GetString(m, "weapClass");
        rule.Caliber = GetString(m, "caliber");
        rule.NamePattern = GetString(m, "namePattern");

        if (m.TryGetProperty("templateIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            rule.TemplateIds = ids.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .ToList();
        }
    }

    public static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static float? GetFloat(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f)
            ? f
            : null;
    }
}
