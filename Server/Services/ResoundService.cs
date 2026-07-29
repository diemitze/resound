using System.Reflection;
using System.Text.Json;
using ResoundServer.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace ResoundServer.Services;

// Everything the config page needs: the weapon list, the packs the player has installed, and
// the saved selections. Singleton so the pages and the client route see the same state.
[Injectable(InjectionType.Singleton)]
public class ResoundService
{
    public const string BuiltinId = "builtin";

    private const string ManifestName = "resound.pack.json";
    private const int MaxScanDepth = 3;

    private static readonly string[] JunkMarkers = ["test", "debug", "dummy", "_dev_", "placeholder"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly DatabaseService _database;
    private readonly ISptLogger<ResoundService> _logger;
    private readonly string _configPath;

    private List<WeaponRow>? _weapons;

    public ResoundWebConfig Config { get; private set; } = new();

    public ResoundService(DatabaseService database, ModHelper modHelper, ISptLogger<ResoundService> logger)
    {
        _database = database;
        _logger = logger;

        var modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _configPath = Path.Join(modFolder, "config.json");

        Load();
    }

    public void Load()
    {
        try
        {
            Config = File.Exists(_configPath)
                ? JsonSerializer.Deserialize<ResoundWebConfig>(File.ReadAllText(_configPath), JsonOptions)
                  ?? new ResoundWebConfig()
                : new ResoundWebConfig();
        }
        catch (Exception e)
        {
            // A broken file should not stop the server booting; defaults are always playable.
            _logger.Warning($"[Resound] config.json unreadable, using defaults: {e.Message}");
            Config = new ResoundWebConfig();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_configPath, JsonSerializer.Serialize(Config, JsonOptions));
            _logger.Info($"[Resound] Saved {Config.Overrides.Count} weapon override(s), active pack '{Config.ActivePack}'.");
        }
        catch (Exception e)
        {
            _logger.Error($"[Resound] Could not write config.json: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Every weapon in the game. WeapClass is only ever set on weapons, so it doubles as the
    /// filter and as the grouping the sound sets already use.
    /// </summary>
    public List<WeaponRow> GetWeapons()
    {
        if (_weapons is not null)
            return _weapons;

        var rows = new List<WeaponRow>();
        var locale = GetEnglishLocale();

        foreach (var (id, item) in _database.GetItems())
        {
            // Categories ("Smg", "Shotgun", "Weapon") are Nodes that real weapons inherit from.
            // They carry a WeapClass like anything else, so they have to be excluded structurally.
            if (!string.Equals(item.Type, "Item", StringComparison.OrdinalIgnoreCase))
                continue;

            var props = item.Properties;
            if (props is null || string.IsNullOrEmpty(props.WeapClass))
                continue;

            // BSG ships debug entries sitting in real weapon categories, "Master Hand"
            // (weapon_test_consumable_items) being the visible one. Filter by pattern rather
            // than by id, because content mods bring their own test items along.
            if (IsJunk(item.Name))
                continue;

            var key = id.ToString();

            // The locale table is the only place the player-facing name lives. props.ShortName
            // and item.Name are internal ("weapon_hk_416a5_556x45"), which is unreadable in a list.
            locale.TryGetValue($"{key} Name", out var full);
            locale.TryGetValue($"{key} ShortName", out var shortName);

            rows.Add(new WeaponRow
            {
                Id = key,
                ParentId = item.Parent.ToString(),
                InternalName = item.Name ?? "",
                Name = Pick(full, shortName, props.ShortName, item.Name, key),
                ShortName = Pick(shortName, full, props.ShortName, key),
                WeapClass = props.WeapClass,
                Caliber = props.AmmoCaliber ?? ""
            });
        }

        rows.Sort((a, b) =>
        {
            var c = string.Compare(a.WeapClass, b.WeapClass, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        _weapons = rows;
        return rows;
    }

    /// <summary>
    /// Packs live client-side under BepInEx/plugins/Resound, so this walks up out of the server
    /// folder to find them. Same machine in every SPT install, but guarded in case that changes.
    /// </summary>
    public List<PackRow> GetPacks()
    {
        var packs = new List<PackRow>();
        var pluginsDir = FindPluginsDir();

        if (pluginsDir is null)
        {
            _logger.Warning("[Resound] Could not locate BepInEx/plugins, pack list will be empty.");
            return packs;
        }

        // The shipped sounds predate the pack format and still live in mapping.json, exactly as
        // the client reads them. Without this the server sees no built-ins and calls every weapon
        // vanilla, which is worse than useless: the page would confidently contradict the game.
        var builtin = ReadBuiltin(pluginsDir);
        if (builtin is not null)
            packs.Add(builtin);

        foreach (var manifest in FindManifests(Path.Join(pluginsDir, "Resound"), 0))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;

                packs.Add(new PackRow
                {
                    Id = GetString(root, "id") ?? Path.GetFileName(Path.GetDirectoryName(manifest)) ?? "unknown",
                    Name = GetString(root, "name") ?? "",
                    Author = GetString(root, "author") ?? "",
                    Priority = root.TryGetProperty("priority", out var p) && p.TryGetInt32(out var pv) ? pv : 0,
                    Rules = ReadRules(root)
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"[Resound] Bad pack manifest {manifest}: {ex.Message}");
            }
        }

        packs.Sort((a, b) =>
        {
            var c = b.Priority.CompareTo(a.Priority);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        return packs;
    }

    private PackRow? ReadBuiltin(string pluginsDir)
    {
        var path = Path.Join(pluginsDir, "Resound", "sounds", "mapping.json");

        if (!File.Exists(path))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            if (!doc.RootElement.TryGetProperty("weapons", out var weapons) ||
                weapons.ValueKind != JsonValueKind.Array)
                return null;

            var pack = new PackRow
            {
                Id = BuiltinId,
                Name = "Resound",
                // Matches the client: the shipped sounds are the floor, so anything the user
                // installs outranks them.
                Priority = -100
            };

            foreach (var w in weapons.EnumerateArray())
            {
                pack.Rules.Add(new PackRule
                {
                    Name = GetString(w, "name") ?? "",
                    SoundSet = GetString(w, "soundSet") ?? "",
                    Enabled = !w.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False,
                    TemplateId = GetString(w, "templateId"),
                    BodyVolume = GetFloat(w, "bodyVolume") ?? 1f,
                    TailVolume = GetFloat(w, "tailVolume") ?? 1f,
                    BodySilencedVolume = GetFloat(w, "bodySilencedVolume"),
                    TailSilencedVolume = GetFloat(w, "tailSilencedVolume"),
                    EventVolume = GetFloat(w, "eventVolume") ?? 1f,
                    DecayBeats = GetFloat(w, "decayBeats")
                });
            }

            return pack;
        }
        catch (Exception e)
        {
            _logger.Warning($"[Resound] Could not read the built-in mapping.json: {e.Message}");
            return null;
        }
    }

    private static List<PackRule> ReadRules(JsonElement root)
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
                DecayBeats = GetFloat(e, "decayBeats")
            };

            if (e.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object)
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

            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>
    /// Works out what a weapon actually sounds like, mirroring the client's rule: an explicit
    /// pin wins, then priority, then specificity. If these two ever disagree the page is lying,
    /// so they have to be changed together.
    /// </summary>
    public Effective Resolve(WeaponRow weapon, List<PackRow> packs)
    {
        var winner = ResolveRule(weapon, packs);

        if (winner is null)
            return new Effective { Source = SoundSource.Vanilla };

        var (pack, rule) = winner.Value;

        return new Effective
        {
            Source = pack.Id == BuiltinId ? SoundSource.Builtin : SoundSource.Addon,
            PackName = pack.Display,
            EntryName = rule.Name
        };
    }

    /// <summary>
    /// The whole decision table, which is what makes the server the single source of truth: the
    /// client stops matching weapons itself and just looks answers up.
    /// </summary>
    public Dictionary<string, ResolvedSound> BuildResolutionMap()
    {
        var packs = GetPacks();
        var map = new Dictionary<string, ResolvedSound>();

        foreach (var weapon in GetWeapons())
        {
            var winner = ResolveRule(weapon, packs);
            if (winner is null)
                continue;

            var (pack, rule) = winner.Value;

            map[weapon.Id] = new ResolvedSound
            {
                PackId = pack.Id,
                SoundSet = rule.SoundSet,
                EntryName = rule.Name,
                BodyVolume = rule.BodyVolume,
                TailVolume = rule.TailVolume,
                BodySilencedVolume = rule.BodySilencedVolume,
                TailSilencedVolume = rule.TailSilencedVolume,
                EventVolume = rule.EventVolume,
                DecayBeats = rule.DecayBeats
            };
        }

        return map;
    }

    // The one place the rules live. Everything else, including the page, reads this.
    private (PackRow Pack, PackRule Rule)? ResolveRule(WeaponRow weapon, List<PackRow> packs)
    {
        if (Config.Overrides.TryGetValue(weapon.Id, out var pinned))
        {
            if (string.Equals(pinned, "Vanilla", StringComparison.OrdinalIgnoreCase))
                return null;

            var pack = packs.FirstOrDefault(p => p.Id.Equals(pinned, StringComparison.OrdinalIgnoreCase));
            var rule = pack?.Rules
                .Where(r => r.Enabled && !string.IsNullOrEmpty(r.SoundSet) && r.Matches(weapon))
                .OrderByDescending(r => r.Specificity)
                .FirstOrDefault();

            // Pinned to a pack that does not actually cover this weapon: nothing to play.
            return pack is not null && rule is not null ? (pack, rule) : null;
        }

        var restricted = !string.Equals(Config.ActivePack, "Auto", StringComparison.OrdinalIgnoreCase);

        PackRow? bestPack = null;
        PackRule? bestRule = null;
        var bestPriority = int.MinValue;
        var bestScore = -1;

        foreach (var pack in packs)
        {
            if (restricted && !pack.Id.Equals(Config.ActivePack, StringComparison.OrdinalIgnoreCase) &&
                pack.Id != BuiltinId)
                continue;

            foreach (var rule in pack.Rules)
            {
                if (!rule.Enabled || string.IsNullOrEmpty(rule.SoundSet) || !rule.Matches(weapon))
                    continue;

                var score = rule.Specificity;
                if (score == 0)
                    continue;

                if (pack.Priority < bestPriority || (pack.Priority == bestPriority && score <= bestScore))
                    continue;

                bestPriority = pack.Priority;
                bestScore = score;
                bestPack = pack;
                bestRule = rule;
            }
        }

        return bestPack is not null && bestRule is not null ? (bestPack, bestRule) : null;
    }

    private Dictionary<string, string> GetEnglishLocale()
    {
        try
        {
            var global = _database.GetLocales().Global;

            if (global.TryGetValue("en", out var lazy) && lazy.Value is { } dict)
                return dict;
        }
        catch (Exception e)
        {
            _logger.Warning($"[Resound] Could not read the locale table, falling back to internal names: {e.Message}");
        }

        return new Dictionary<string, string>();
    }

    private static bool IsJunk(string? internalName)
    {
        return !string.IsNullOrEmpty(internalName) &&
               JunkMarkers.Any(m => internalName.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static string Pick(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
                return c.Trim();
        }

        return "";
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static float? GetFloat(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f)
            ? f
            : null;
    }

    private static string? FindPluginsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, "BepInEx", "plugins");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return null;
    }

    private static IEnumerable<string> FindManifests(string dir, int depth)
    {
        if (depth > MaxScanDepth || !Directory.Exists(dir))
            yield break;

        // A manifest claims its whole folder, so stop rather than nest packs.
        var candidate = Path.Join(dir, ManifestName);
        if (File.Exists(candidate))
        {
            yield return candidate;
            yield break;
        }

        string[] subs;
        try
        {
            subs = Directory.GetDirectories(dir);
        }
        catch
        {
            yield break;
        }

        foreach (var sub in subs)
        {
            // The built-in audio, hundreds of folders and never a manifest.
            if (depth == 0 && string.Equals(Path.GetFileName(sub), "sounds", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var found in FindManifests(sub, depth + 1))
                yield return found;
        }
    }
}
