using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ResoundServer.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;

namespace ResoundServer.Services;

/// <summary>Writes a draft to disk as a pack the plugin reads without the server mod.</summary>
[Injectable(InjectionType.Singleton)]
public class PackBuilderService
{
    private static readonly string[] Slots =
    [
        "body", "tail", "doublet", "body_silenced", "tail_silenced", "doublet_silenced"
    ];

    private readonly PackValidator _validator;
    private readonly ISptLogger<PackBuilderService> _logger;

    public PackBuilderService(PackValidator validator, ISptLogger<PackBuilderService> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    /// <summary>Where packs are written. Null means the live BepInEx/plugins/Resound.</summary>
    public string? InstallRoot { get; set; }

    /// <summary>Where a draft with this id would be written.</summary>
    public string? TargetDirectory(string id)
    {
        var root = InstallRoot ?? SptPaths.FindResoundRoot();
        return root is null ? null : Path.Join(root, Slug(id));
    }

    /// <summary>Everything wrong with a draft, as the same problems the installed-pack check reports.</summary>
    public List<PackProblem> Check(PackDraft draft, bool overwrite = false)
    {
        var problems = new List<PackProblem>();

        var id = Slug(draft.Id);

        if (string.IsNullOrEmpty(id))
        {
            problems.Add(Problem(null, "the pack needs an id. It names your pack in the log and in " +
                                       "the pack selector, and it is the folder people install."));
        }
        else if (!overwrite)
        {
            // Usually a second run of the same build, but it could be someone else's pack.
            var target = TargetDirectory(id);

            if (target is not null && Directory.Exists(target))
                problems.Add(Problem(null, $"'{id}' is already installed at {target}.",
                    "Pick another id, or build again over the top of it."));
        }

        if (draft.Sets.Count == 0)
            problems.Add(Problem(null, "the pack has no sound sets, so it claims no weapons."));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in draft.Sets)
        {
            var label = Label(set);
            var folder = Slug(set.SoundSet);

            if (string.IsNullOrEmpty(folder))
            {
                problems.Add(Problem(label, "no sound set name, so there is nowhere to put the audio."));
                continue;
            }

            // Shared sets are fine; different names landing in one folder would silently merge them.
            if (!seen.Add(folder) && !set.SoundSet.Equals(folder, StringComparison.Ordinal))
                problems.Add(Problem(label, $"'{set.SoundSet}' becomes the folder '{folder}', which " +
                                            "another set in this pack already uses."));

            if (set.Rule.Specificity == 0)
                problems.Add(Problem(label, "this set claims no weapons.",
                    "Pick at least one weapon, or a weapon class, for it."));

            foreach (var file in set.Files)
                CheckFile(problems, label, file);
        }

        return problems;
    }

    private void CheckFile(List<PackProblem> problems, string label, StagedFile file)
    {
        if (!File.Exists(file.SourcePath))
        {
            problems.Add(Problem(label, $"'{file.TargetName}' is not there any more.", file.SourcePath));
            return;
        }

        if (!IsSlot(file.Slot))
        {
            problems.Add(Problem(label, $"'{file.Slot}' is not a slot.",
                "Use body, tail, doublet, one of the _silenced variants, or events/<EventName>."));
            return;
        }

        if (!Path.GetExtension(file.SourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(Problem(label, $"'{file.TargetName}' is not a WAV, and Resound only reads WAV.",
                $"ffmpeg -i \"{file.TargetName}\" -ac 1 -ar 48000 -c:a pcm_s16le " +
                $"\"{Path.GetFileNameWithoutExtension(file.TargetName)}.wav\""));
            return;
        }

        var info = WavInspector.Inspect(file.SourcePath);

        if (!info.Ok)
            problems.Add(Problem(label, $"{file.TargetName}: {info.Problem}"));
    }

    /// <summary>Writes the draft. Never deletes; leftovers show up as unused folders in the check.</summary>
    public BuildResult Build(PackDraft draft, bool overwrite = false)
    {
        var result = new BuildResult { Refusals = Check(draft, overwrite) };

        if (result.Refusals.Count > 0)
            return result;

        var id = Slug(draft.Id);
        var target = TargetDirectory(id);

        if (target is null)
        {
            result.Refusals.Add(Problem(null,
                "BepInEx/plugins could not be found from the server folder, so there is nowhere " +
                "to install the pack."));
            return result;
        }

        result.Directory = target;

        try
        {
            var soundsRoot = Path.Join(target, "sounds");
            Directory.CreateDirectory(soundsRoot);

            foreach (var set in draft.Sets)
            {
                var setDir = Path.Join(soundsRoot, Slug(set.SoundSet));

                // Every slot folder, even empty ones, so the layout explains itself.
                foreach (var slot in Slots)
                    Directory.CreateDirectory(Path.Join(setDir, slot));

                foreach (var file in set.Files)
                {
                    var dir = Path.Join(setDir, SlotPath(file.Slot));
                    Directory.CreateDirectory(dir);

                    var destination = Path.Join(dir, file.TargetName);

                    // A reopened pack points at its own files, and copying a file onto itself throws.
                    if (Path.GetFullPath(file.SourcePath).Equals(Path.GetFullPath(destination),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Copy(file.SourcePath, destination, true);
                    result.FilesCopied++;
                }
            }

            // Manifest last, so a build that dies half way leaves nothing the client loads.
            File.WriteAllText(Path.Join(target, PackManifest.FileName), Manifest(draft, id));
            result.Written = true;
        }
        catch (Exception e)
        {
            result.Refusals.Add(Problem(null, $"the pack could not be written: {e.Message}", target));
            _logger.Error($"[Resound] Could not build pack '{id}': {e}");
            return result;
        }

        _logger.Info($"[Resound] Built pack '{id}': {draft.Sets.Count} set(s), {result.FilesCopied} file(s) into {target}");

        result.Health = _validator.Check(AsRow(draft, id, target));
        return result;
    }

    /// <summary>Reads an installed pack back into a draft. Match blocks round-trip untouched, namePattern included.</summary>
    public PackDraft Open(PackRow pack)
    {
        var draft = new PackDraft
        {
            Id = pack.Id,
            Name = pack.Name,
            Author = pack.Author,
            Priority = pack.Priority,
            AutoLevel = pack.AutoLevel
        };

        foreach (var rule in pack.Rules)
        {
            draft.Sets.Add(new SetDraft
            {
                Rule = Clone(rule),

                // The reader flattens inheritance, so only values differing from the pack's are real overrides.
                AutoLevel = rule.AutoLevel == pack.AutoLevel ? null : rule.AutoLevel
            });
        }

        return draft;
    }

    /// <summary>Every audio file already in a set, with the slot it sits in.</summary>
    public List<(string Path, string Slot)> ExistingAudio(string soundsRoot, string soundSet)
    {
        var found = new List<(string, string)>();
        var setDir = Path.Join(soundsRoot, soundSet);

        if (!Directory.Exists(setDir))
            return found;

        foreach (var slot in Slots)
            Collect(Path.Join(setDir, slot), slot);

        // Loose files at the top of the set folder are the body, exactly as the plugin reads them.
        Collect(setDir, "body");

        var eventsDir = Path.Join(setDir, "events");

        if (Directory.Exists(eventsDir))
        {
            foreach (var sub in Directory.GetDirectories(eventsDir))
                Collect(sub, "events/" + Path.GetFileName(sub));
        }

        return found;

        void Collect(string dir, string slot)
        {
            if (!Directory.Exists(dir))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                             .Where(AudioConverter.LooksLikeAudio)
                             .OrderBy(f => f))
                    found.Add((file, slot));
            }
            catch (Exception e)
            {
                _logger.Warning($"[Resound] Could not read {dir}: {e.Message}");
            }
        }
    }

    /// <summary>Zips a pack with BepInEx/plugins/Resound/id/ inside, so installing is one extract.</summary>
    public string Export(string packDirectory, string? destination = null)
    {
        if (!Directory.Exists(packDirectory))
            throw new DirectoryNotFoundException($"No pack at {packDirectory}");

        var id = Path.GetFileName(packDirectory.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));

        destination ??= Path.Join(Path.GetDirectoryName(packDirectory)!, id + ".zip");

        // Rewritten rather than appended to, or a second export doubles every entry.
        if (File.Exists(destination))
            File.Delete(destination);

        var prefix = $"BepInEx/plugins/Resound/{id}/";

        using (var zip = ZipFile.Open(destination,
                   ZipArchiveMode.Create))
        {
            foreach (var file in Directory.GetFiles(packDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(packDirectory, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, prefix + relative);
            }
        }

        _logger.Info($"[Resound] Exported pack '{id}' to {destination}");

        return destination;
    }

    /// <summary>The manifest as JSON, defaults left out so it reads like the hand-written example.</summary>
    public string Manifest(PackDraft draft, string? id = null)
    {
        var root = new JsonObject
        {
            ["id"] = Slug(id ?? draft.Id),
            ["name"] = string.IsNullOrWhiteSpace(draft.Name) ? Slug(draft.Id) : draft.Name.Trim(),
            ["author"] = string.IsNullOrWhiteSpace(draft.Author) ? "unknown" : draft.Author.Trim(),
            ["apiVersion"] = 1
        };

        if (draft.Priority != 0) root["priority"] = draft.Priority;
        if (!draft.AutoLevel) root["autoLevel"] = false;

        var entries = new JsonArray();

        foreach (var set in draft.Sets)
            entries.Add(Entry(set));

        root["entries"] = entries;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Entry(SetDraft set)
    {
        var rule = set.Rule;

        var entry = new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(rule.Name) ? Slug(set.SoundSet) : rule.Name.Trim(),
            ["match"] = Match(rule),
            ["soundSet"] = Slug(set.SoundSet)
        };

        if (rule.BodyVolume != 1f) entry["bodyVolume"] = rule.BodyVolume;
        if (rule.TailVolume != 1f) entry["tailVolume"] = rule.TailVolume;
        if (rule.BodySilencedVolume is { } bsv) entry["bodySilencedVolume"] = bsv;
        if (rule.TailSilencedVolume is { } tsv) entry["tailSilencedVolume"] = tsv;
        if (rule.EventVolume != 1f) entry["eventVolume"] = rule.EventVolume;
        if (rule.DecayBeats is { } db) entry["decayBeats"] = db;
        if (set.AutoLevel is { } al) entry["autoLevel"] = al;
        if (!rule.Enabled) entry["enabled"] = false;

        return entry;
    }

    private static JsonObject Match(PackRule rule)
    {
        var match = new JsonObject();

        if (!string.IsNullOrWhiteSpace(rule.TemplateId))
            match["templateId"] = rule.TemplateId.Trim();

        if (rule.TemplateIds is { Count: > 0 })
        {
            var ids = new JsonArray();
            foreach (var id in rule.TemplateIds.Where(i => !string.IsNullOrWhiteSpace(i)))
                ids.Add(id.Trim());

            if (ids.Count > 0)
                match["templateIds"] = ids;
        }

        if (!string.IsNullOrWhiteSpace(rule.ParentId)) match["parentId"] = rule.ParentId.Trim();
        if (!string.IsNullOrWhiteSpace(rule.WeapClass)) match["weapClass"] = rule.WeapClass.Trim();
        if (!string.IsNullOrWhiteSpace(rule.Caliber)) match["caliber"] = rule.Caliber.Trim();
        if (!string.IsNullOrWhiteSpace(rule.NamePattern)) match["namePattern"] = rule.NamePattern.Trim();

        return match;
    }

    /// <summary>The draft as an installed pack, so the validator runs its usual path over it.</summary>
    private static PackRow AsRow(PackDraft draft, string id, string directory)
    {
        var row = new PackRow
        {
            Id = id,
            Name = draft.Name,
            Author = draft.Author,
            Priority = draft.Priority,
            AutoLevel = draft.AutoLevel,
            Directory = directory
        };

        foreach (var set in draft.Sets)
        {
            var rule = Clone(set.Rule);
            rule.SoundSet = Slug(set.SoundSet);
            rule.AutoLevel = set.AutoLevel ?? draft.AutoLevel;
            row.Rules.Add(rule);
        }

        return row;
    }

    private static PackRule Clone(PackRule r)
    {
        return new PackRule
        {
            Name = r.Name,
            SoundSet = r.SoundSet,
            Enabled = r.Enabled,
            TemplateId = r.TemplateId,
            TemplateIds = r.TemplateIds is null ? null : [.. r.TemplateIds],
            ParentId = r.ParentId,
            WeapClass = r.WeapClass,
            Caliber = r.Caliber,
            NamePattern = r.NamePattern,
            BodyVolume = r.BodyVolume,
            TailVolume = r.TailVolume,
            BodySilencedVolume = r.BodySilencedVolume,
            TailSilencedVolume = r.TailSilencedVolume,
            EventVolume = r.EventVolume,
            DecayBeats = r.DecayBeats
        };
    }

    /// <summary>Which weapons an entry claims. The list is passed in to keep the builder off the database.</summary>
    public static List<WeaponRow> Claims(PackRule rule, IEnumerable<WeaponRow> weapons)
    {
        return rule.Specificity == 0 ? [] : weapons.Where(rule.Matches).ToList();
    }

    private static bool IsSlot(string slot)
    {
        if (Slots.Contains(slot, StringComparer.OrdinalIgnoreCase))
            return true;

        if (!slot.StartsWith("events/", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = slot["events/".Length..];

        return name.Length > 0 && name.IndexOfAny(['/', '\\']) < 0 &&
               name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static string SlotPath(string slot)
    {
        return slot.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>Lowercase, no spaces: safe in a URL, a log line and a zip.</summary>
    public static string Slug(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        var chars = s.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' ? c : '-');

        var slug = new string(chars.ToArray());

        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        return slug.Trim('-');
    }

    private static string Label(SetDraft set)
    {
        if (!string.IsNullOrWhiteSpace(set.Rule.Name)) return set.Rule.Name.Trim();
        if (!string.IsNullOrWhiteSpace(set.SoundSet)) return set.SoundSet.Trim();
        return "(unnamed set)";
    }

    private static PackProblem Problem(string? entry, string message, string? fix = null)
    {
        return new PackProblem { Entry = entry, Message = message, Fix = fix };
    }
}
