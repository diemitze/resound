using System.Reflection;
using System.Text.Json;
using ResoundServer.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ResoundServer.Services;

// State shared by the pages and the client routes: weapons, installed packs, saved picks.
[Injectable(InjectionType.Singleton)]
public class ResoundService
{
    public const string BuiltinId = "builtin";

    private const int MaxScanDepth = 3;

    private static readonly string[] JunkMarkers = ["test", "debug", "dummy", "_dev_", "placeholder"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly TemplateTable _templates;
    private readonly LocaleTable _locales;
    private readonly ISptLogger<ResoundService> _logger;
    private readonly string _configPath;

    private List<WeaponRow>? _weapons;

    public ResoundWebConfig Config { get; private set; } = new();

    /// <summary>Where packs are read from. Null finds the real BepInEx/plugins; tests point it elsewhere.</summary>
    public string? PluginsRootOverride { get; set; }

    public ResoundService(TemplateTable templates, LocaleTable locales, ModHelper modHelper, ISptLogger<ResoundService> logger)
    {
        _templates = templates;
        _locales = locales;
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

    /// <summary>Every weapon in the game. Only weapons carry a WeapClass.</summary>
    public List<WeaponRow> GetWeapons()
    {
        if (_weapons is not null)
            return _weapons;

        var rows = new List<WeaponRow>();
        var locale = GetEnglishLocale();

        foreach (var (id, item) in _templates.Items)
        {
            // Category nodes carry a WeapClass too, so they are excluded structurally.
            if (!string.Equals(item.Type, "Item", StringComparison.OrdinalIgnoreCase))
                continue;

            var props = item.Properties;
            if (props is null || string.IsNullOrEmpty(props.WeapClass))
                continue;

            // BSG and content mods ship test items in real categories, so filter by pattern rather than id.
            if (IsJunk(item.Name))
                continue;

            var key = id.ToString();

            // Only the locale table has the player-facing name; ShortName is internal.
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

    /// <summary>Packs are client-side, so walk up out of the server folder to find them.</summary>
    public List<PackRow> GetPacks()
    {
        var packs = new List<PackRow>();
        var pluginsDir = PluginsRootOverride ?? SptPaths.FindPluginsDir();

        if (pluginsDir is null)
        {
            _logger.Warning("[Resound] Could not locate BepInEx/plugins, pack list will be empty.");
            return packs;
        }

        // The shipped sounds are still in mapping.json; without them every weapon would look vanilla.
        var builtin = ReadBuiltin(pluginsDir);
        if (builtin is not null)
            packs.Add(builtin);

        foreach (var manifest in FindManifests(Path.Join(pluginsDir, "Resound"), 0))
        {
            try
            {
                packs.Add(PackManifest.Read(manifest));
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
                Directory = Path.Join(pluginsDir, "Resound"),
                Name = "Resound",
                // The shipped sounds are mixed by hand, so their volume numbers are literal.
                AutoLevel = false,
                // The floor, as on the client.
                Priority = -100
            };

            foreach (var w in weapons.EnumerateArray())
            {
                var rule = new PackRule
                {
                    Name = PackManifest.GetString(w, "name") ?? "",
                    SoundSet = PackManifest.GetString(w, "soundSet") ?? "",
                    Enabled = !w.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False,
                    BodyVolume = PackManifest.GetFloat(w, "bodyVolume") ?? 1f,
                    TailVolume = PackManifest.GetFloat(w, "tailVolume") ?? 1f,
                    BodySilencedVolume = PackManifest.GetFloat(w, "bodySilencedVolume"),
                    TailSilencedVolume = PackManifest.GetFloat(w, "tailSilencedVolume"),
                    EventVolume = PackManifest.GetFloat(w, "eventVolume") ?? 1f,
                    DecayBeats = PackManifest.GetFloat(w, "decayBeats"),
                    AutoLevel = w.TryGetProperty("autoLevel", out var al) && al.ValueKind == JsonValueKind.True
                };

                // The match fields sit on the entry itself here, not under "match".
                PackManifest.ApplyMatch(w, rule);
                pack.Rules.Add(rule);
            }

            return pack;
        }
        catch (Exception e)
        {
            _logger.Warning($"[Resound] Could not read the built-in mapping.json: {e.Message}");
            return null;
        }
    }

    /// <summary>What a weapon plays: pin, then priority, then specificity. Must match the client's rule.</summary>
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

    /// <summary>The full decision table the client looks answers up in.</summary>
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
                DecayBeats = rule.DecayBeats,
                AutoLevel = rule.AutoLevel
            };
        }

        return map;
    }

    /// <summary>The winning pack and entry with the numbers behind them, for the builder's outrank check.</summary>
    public (PackRow Pack, PackRule Rule)? Winner(WeaponRow weapon, List<PackRow> packs)
    {
        return ResolveRule(weapon, packs);
    }

    /// <summary>Every pack with a sound for this weapon, best first. Offering one that does not cover it would pin the gun to silence.</summary>
    public List<(PackRow Pack, PackRule Rule)> Candidates(WeaponRow weapon, List<PackRow> packs)
    {
        var found = new List<(PackRow Pack, PackRule Rule)>();

        foreach (var pack in packs)
        {
            // Every set, not just the winner: a second set for the same gun is how a pack offers a choice.
            var rules = pack.Rules
                .Where(r => r.Enabled && !string.IsNullOrEmpty(r.SoundSet) && r.Specificity > 0 && r.Matches(weapon))
                .GroupBy(r => r.SoundSet, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.Specificity).First());

            foreach (var rule in rules)
                found.Add((pack, rule));
        }

        // The order Auto would pick them in, so the first chip is the one Auto already plays.
        return found
            .OrderByDescending(c => c.Pack.Priority)
            .ThenByDescending(c => c.Rule.Specificity)
            .ThenBy(c => c.Pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether these numbers would take a weapon off its current holder: priority, specificity, then pack id.</summary>
    public static bool Outranks(int priority, int specificity, string id, PackRow held, PackRule by)
    {
        if (priority != held.Priority)
            return priority > held.Priority;

        if (specificity != by.Specificity)
            return specificity > by.Specificity;

        return string.Compare(id, held.Id, StringComparison.OrdinalIgnoreCase) < 0;
    }

    /// <summary>A pin is a pack id, or packId::soundSet when the pack offers a choice. A bare id means its best entry.</summary>
    public const string PinSeparator = "::";

    public static string Pin(string packId, string? soundSet)
    {
        return string.IsNullOrEmpty(soundSet) ? packId : packId + PinSeparator + soundSet;
    }

    public static (string PackId, string? SoundSet) SplitPin(string pin)
    {
        var at = pin.IndexOf(PinSeparator, StringComparison.Ordinal);
        return at < 0
            ? (pin, null)
            : (pin[..at], pin[(at + PinSeparator.Length)..]);
    }

    // The one place the rules live. Everything else, including the page, reads this.
    private (PackRow Pack, PackRule Rule)? ResolveRule(WeaponRow weapon, List<PackRow> packs)
    {
        if (Config.Overrides.TryGetValue(weapon.Id, out var pinned))
        {
            if (string.Equals(pinned, "Vanilla", StringComparison.OrdinalIgnoreCase))
                return null;

            var (pinnedPack, pinnedSet) = SplitPin(pinned);

            var pack = packs.FirstOrDefault(p => p.Id.Equals(pinnedPack, StringComparison.OrdinalIgnoreCase));
            var rule = pack?.Rules
                .Where(r => r.Enabled && !string.IsNullOrEmpty(r.SoundSet) && r.Matches(weapon))
                .Where(r => pinnedSet is null || r.SoundSet.Equals(pinnedSet, StringComparison.OrdinalIgnoreCase))
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
            var global = _locales.Global;

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

    private static IEnumerable<string> FindManifests(string dir, int depth)
    {
        if (depth > MaxScanDepth || !Directory.Exists(dir))
            yield break;

        // A manifest claims its whole folder, so stop rather than nest packs.
        var candidate = Path.Join(dir, PackManifest.FileName);
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
