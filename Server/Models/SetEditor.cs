namespace ResoundServer.Models;

/// <summary>How someone said which weapons a set claims.</summary>
public enum MatchMode
{
    /// <summary>Weapons picked one at a time.</summary>
    Weapons,

    /// <summary>A whole class, optionally narrowed to one calibre.</summary>
    Class,

    /// <summary>A hand-written match the page has no control for. Shown and written back exactly as found.</summary>
    Advanced
}

/// <summary>One sound set as the builder edits it. Kept out of the page because the claims preview must not be quietly wrong.</summary>
public class SetEditor
{
    private readonly IReadOnlyList<WeaponRow> _weapons;
    private string? _suggestedName;

    public SetEditor(IReadOnlyList<WeaponRow> weapons, string? weapClass = null)
    {
        _weapons = weapons;

        Draft.Rule.WeapClass = weapClass ?? weapons.Select(w => w.WeapClass).FirstOrDefault();
        Rename();
    }

    /// <summary>A set read off disk. The rule is kept as is, so a hand-written match survives a save.</summary>
    public SetEditor(IReadOnlyList<WeaponRow> weapons, SetDraft draft)
    {
        _weapons = weapons;
        Draft = draft;

        Mode = InferMode(draft.Rule);

        if (Mode == MatchMode.Weapons)
        {
            if (!string.IsNullOrEmpty(draft.Rule.TemplateId))
                Weapons.Add(draft.Rule.TemplateId);

            foreach (var id in draft.Rule.TemplateIds ?? [])
                Weapons.Add(id);
        }

        // Everything about it was named by whoever wrote it, so nothing here renames anything.
        FolderNamed = true;
        _suggestedName = null;
    }

    /// <summary>Which simple mode could have produced this match, or Advanced.</summary>
    private static MatchMode InferMode(PackRule rule)
    {
        var hasIds = !string.IsNullOrEmpty(rule.TemplateId) || rule.TemplateIds is { Count: > 0 };
        var hasClass = !string.IsNullOrEmpty(rule.WeapClass);
        var hasOther = !string.IsNullOrEmpty(rule.ParentId) || !string.IsNullOrEmpty(rule.NamePattern);

        if (hasOther)
            return MatchMode.Advanced;

        if (hasIds)
            return hasClass || !string.IsNullOrEmpty(rule.Caliber) ? MatchMode.Advanced : MatchMode.Weapons;

        return MatchMode.Class;
    }

    /// <summary>How the match reads when the page cannot offer a control for it.</summary>
    public string MatchSummary()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(Draft.Rule.TemplateId)) parts.Add("templateId " + Draft.Rule.TemplateId);
        if (Draft.Rule.TemplateIds is { Count: > 0 }) parts.Add($"{Draft.Rule.TemplateIds.Count} template ids");
        if (!string.IsNullOrEmpty(Draft.Rule.ParentId)) parts.Add("parentId " + Draft.Rule.ParentId);
        if (!string.IsNullOrEmpty(Draft.Rule.WeapClass)) parts.Add("weapClass " + Draft.Rule.WeapClass);
        if (!string.IsNullOrEmpty(Draft.Rule.Caliber)) parts.Add("caliber " + Draft.Rule.Caliber);
        if (!string.IsNullOrEmpty(Draft.Rule.NamePattern)) parts.Add("namePattern " + Draft.Rule.NamePattern);

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    public SetDraft Draft { get; } = new();

    /// <summary>Template ids picked by hand. Only meaningful in <see cref="MatchMode.Weapons"/>.</summary>
    public HashSet<string> Weapons { get; } = new();

    public MatchMode Mode { get; private set; } = MatchMode.Class;

    /// <summary>True once someone has typed a folder name, which stops it following the selection.</summary>
    public bool FolderNamed { get; private set; }

    /// <summary>Everything dropped on the set, refused files included, so a refusal is visible.</summary>
    public List<Services.StagedCandidate> Audio { get; } = new();

    public IEnumerable<Services.StagedCandidate> Accepted => Audio.Where(c => c.Ok);
    public IEnumerable<Services.StagedCandidate> Rejected => Audio.Where(c => !c.Ok);

    public int Clips => Audio.Count(c => c.Ok);

    /// <summary>The one slot that must have audio; without it the gun is unchanged.</summary>
    public bool HasBody => Audio.Any(c => c.Ok && c.Slot == "body");

    /// <summary>True while the page is showing this set's name and folder as editable fields.</summary>
    public bool Renaming { get; set; }

    /// <summary>The folder the page last scanned for this set. Remembered so a retype is not needed.</summary>
    public string? ScanPath { get; set; }

    /// <summary>Slots this set actually uses, so the page can offer the event ones it was given.</summary>
    public IEnumerable<string> SlotsInUse =>
        Accepted.Select(c => c.Slot).Distinct().OrderBy(s => s);

    public void Add(IEnumerable<Services.StagedCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            // Scanning the same folder twice is a normal thing to do by accident.
            if (candidate.File is not null && Audio.Any(c => c.File?.SourcePath == candidate.File.SourcePath))
                continue;

            Audio.Add(candidate);
        }

        SyncFiles();
    }

    public void Remove(Services.StagedCandidate candidate)
    {
        Audio.Remove(candidate);
        SyncFiles();
    }

    public void Reslot(Services.StagedCandidate candidate, string slot)
    {
        candidate.Slot = slot;
        candidate.Doubt = slot.StartsWith("events/", StringComparison.OrdinalIgnoreCase)
            ? null
            : Services.SlotClassifier.Doubt(candidate.OriginalName);
        SyncFiles();
    }

    /// <summary>The draft only carries loadable files, so the build and the page cannot disagree.</summary>
    private void SyncFiles()
    {
        Draft.Files = Accepted.Select(c => c.File!).ToList();
    }

    public void SetMode(MatchMode mode)
    {
        // Leaving Advanced would lose a match block the page cannot rebuild.
        if (Mode == MatchMode.Advanced || mode == MatchMode.Advanced)
            return;

        Mode = mode;

        if (mode == MatchMode.Class)
        {
            Draft.Rule.TemplateId = null;
            Draft.Rule.TemplateIds = null;
            Draft.Rule.WeapClass ??= _weapons.Select(w => w.WeapClass).FirstOrDefault();
        }
        else
        {
            // Class and ids are ANDed, so a leftover class would narrow the hand-picked weapons.
            Draft.Rule.WeapClass = null;
            Draft.Rule.Caliber = null;
            ApplyWeapons();
        }

        Rename();
    }

    public void SetClass(string? weapClass)
    {
        Draft.Rule.WeapClass = weapClass;

        // The calibre list is per class, so one that no longer applies has to go with it.
        if (!string.IsNullOrEmpty(Draft.Rule.Caliber) && !Calibres(weapClass).Contains(Draft.Rule.Caliber))
            Draft.Rule.Caliber = null;

        Rename();
    }

    public void SetCaliber(string? caliber)
    {
        Draft.Rule.Caliber = string.IsNullOrEmpty(caliber) ? null : caliber;
        Rename();
    }

    public void WeaponsChanged()
    {
        ApplyWeapons();
        Rename();
    }

    public void NameFolder(string folder)
    {
        Draft.SoundSet = folder;
        FolderNamed = true;
    }

    /// <summary>Which weapons this set would actually take, right now.</summary>
    public List<WeaponRow> Claims() => Services.PackBuilderService.Claims(Draft.Rule, _weapons);

    /// <summary>The calibres worth offering for a class. Empty class means every calibre.</summary>
    public List<string> Calibres(string? weapClass)
    {
        return _weapons
            .Where(w => string.IsNullOrEmpty(weapClass) || w.WeapClass == weapClass)
            .Select(w => w.Caliber)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>One weapon writes templateId, several write templateIds.</summary>
    private void ApplyWeapons()
    {
        var ids = Weapons.ToList();

        Draft.Rule.TemplateId = ids.Count == 1 ? ids[0] : null;
        Draft.Rule.TemplateIds = ids.Count > 1 ? ids : null;
    }

    /// <summary>Name and folder follow the selection until typed over. Tracked per set so cards do not freeze each other.</summary>
    private void Rename()
    {
        if (string.IsNullOrWhiteSpace(Draft.Rule.Name) || Draft.Rule.Name == _suggestedName)
        {
            Draft.Rule.Name = Describe();
            _suggestedName = Draft.Rule.Name;
        }

        if (!FolderNamed)
            Draft.SoundSet = Services.PackBuilderService.Slug(Suggestion());
    }

    private string Suggestion()
    {
        if (Mode == MatchMode.Class)
        {
            var suggestion = Draft.Rule.WeapClass ?? "set";

            if (!string.IsNullOrEmpty(Draft.Rule.Caliber))
                suggestion += "-" + Draft.Rule.Caliber.Replace("Caliber", "");

            return suggestion;
        }

        var first = _weapons.FirstOrDefault(w => Weapons.Contains(w.Id));

        if (first is null)
            return "set";

        if (Weapons.Count == 1)
            return first.ShortName;

        // A family picked together shares a stem worth naming the folder after.
        var names = _weapons.Where(w => Weapons.Contains(w.Id)).Select(w => w.ShortName).ToList();
        var stem = names.Aggregate((a, b) => CommonPrefix(a, b)).TrimEnd('-', ' ', '_', '.');

        return stem.Length >= 2 ? stem : $"{first.ShortName}-and-{Weapons.Count - 1}-more";
    }

    private static string CommonPrefix(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && char.ToUpperInvariant(a[i]) == char.ToUpperInvariant(b[i]))
            i++;
        return a[..i];
    }

    /// <summary>The label, in the words someone would use out loud.</summary>
    public string Describe()
    {
        if (Mode == MatchMode.Advanced)
            return string.IsNullOrWhiteSpace(Draft.Rule.Name) ? Draft.SoundSet : Draft.Rule.Name;

        if (Mode == MatchMode.Class)
        {
            var c = Draft.Rule.Caliber;

            return string.IsNullOrEmpty(c)
                ? "Every " + (Draft.Rule.WeapClass is null ? "weapon" : Services.Labels.Class(Draft.Rule.WeapClass).ToLowerInvariant())
                : $"Every {Services.Labels.Class(Draft.Rule.WeapClass).ToLowerInvariant()} in {Services.Labels.Calibre(c)}";
        }

        var names = _weapons.Where(w => Weapons.Contains(w.Id)).Select(w => w.ShortName).ToList();

        return names.Count switch
        {
            0 => "New set",
            1 => names[0],
            _ => $"{names[0]} and {names.Count - 1} more"
        };
    }
}
