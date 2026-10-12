using ResoundServer.Models;
using SPTarkov.DI.Annotations;

namespace ResoundServer.Services;

/// <summary>The pack-report.txt checks, returned as data for the pages and the builder.</summary>
[Injectable(InjectionType.Singleton)]
public class PackValidator
{
    private static readonly string[] Slots =
    [
        "body", "tail", "doublet", "body_silenced", "tail_silenced", "doublet_silenced"
    ];

    // Extensions people try to ship. Readmes and licences beside the audio are left alone.
    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".opus", ".wv", ".vsnd"
    ];

    public List<PackHealth> CheckAll(IEnumerable<PackRow> packs)
    {
        return packs.Select(Check).ToList();
    }

    public PackHealth Check(PackRow pack)
    {
        var health = new PackHealth
        {
            PackId = pack.Id,
            PackName = pack.Display,
            Directory = pack.Directory
        };

        if (string.IsNullOrEmpty(pack.Directory))
        {
            Problem(health, null, null, "the pack's folder could not be located, so nothing in it was checked.");
            return health;
        }

        var soundsRoot = pack.SoundsRoot;

        if (!System.IO.Directory.Exists(soundsRoot))
        {
            Problem(health, null, null,
                "there is no 'sounds' folder here, so no entry can find its audio.",
                $"Every soundSet is a folder under {soundsRoot}");
            return health;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in pack.Rules)
            CheckEntry(health, rule, soundsRoot, used);

        CheckUnusedSets(health, soundsRoot, used);

        return health;
    }

    private void CheckEntry(PackHealth health, PackRule rule, string soundsRoot, HashSet<string> used)
    {
        var label = Label(rule);

        if (!rule.Enabled)
        {
            // Still claim the folder, or parking an entry reports its set as unused.
            if (!string.IsNullOrEmpty(rule.SoundSet))
                used.Add(rule.SoundSet);

            Note(health, label, null, "disabled, so this entry is skipped entirely.");
            return;
        }

        if (rule.Specificity == 0)
        {
            Problem(health, label, null, "no match block, so this entry claims no weapons.",
                "Give it a \"match\" with a weapClass, templateId or namePattern.");
            return;
        }

        if (string.IsNullOrEmpty(rule.SoundSet))
        {
            Problem(health, label, null, "no soundSet, so there is nothing to play.");
            return;
        }

        used.Add(rule.SoundSet);

        var set = new SetHealth { Name = rule.SoundSet, EntryName = label };
        health.Sets.Add(set);

        var setDir = Path.Join(soundsRoot, rule.SoundSet);

        if (!System.IO.Directory.Exists(setDir))
        {
            var near = Nearest(soundsRoot, rule.SoundSet);

            Problem(health, label, null,
                $"there is no folder called '{rule.SoundSet}' under sounds\\",
                near is null
                    ? "Create it, or point soundSet at a folder that exists."
                    : $"Did you mean '{near}'? Folder names are matched exactly.");
            return;
        }

        set.Exists = true;

        foreach (var slot in Slots)
        {
            var dir = Path.Join(setDir, slot);

            // Loose wavs at the top of the set folder count as the body.
            if (slot == "body" && !System.IO.Directory.Exists(dir))
            {
                CheckSlot(health, set, label, "body (loose files)", setDir, false);
                continue;
            }

            CheckSlot(health, set, label, slot, dir, true);
        }

        CheckEvents(health, set, label, Path.Join(setDir, "events"));

        if (set.Clips == 0)
            Problem(health, label, null,
                "this set has no usable audio at all, so the weapon stays vanilla.");
    }

    private void CheckEvents(PackHealth health, SetHealth set, string label, string eventsDir)
    {
        if (!System.IO.Directory.Exists(eventsDir))
            return;

        string[] subs;
        try
        {
            subs = System.IO.Directory.GetDirectories(eventsDir);
        }
        catch (Exception e)
        {
            Problem(health, label, "events", $"the events folder could not be read: {e.Message}");
            return;
        }

        if (subs.Length == 0)
        {
            Problem(health, label, "events", "events/ is empty, so no reload or handling sound is replaced.",
                "Each one goes in a subfolder named after the animation event, e.g. events/SndMagOut/. " +
                "Turn on 'Log sound event names' and draw the weapon to see the real list.");
            return;
        }

        foreach (var sub in subs)
        {
            var name = "events/" + Path.GetFileName(sub);

            // An empty slot folder keeps vanilla on purpose; an empty event folder never does.
            if (!HasAudio(sub))
            {
                Problem(health, label, name, "there are no audio files in here, so this event stays vanilla.");
                continue;
            }

            CheckSlot(health, set, label, name, sub, true);
        }
    }

    private static bool HasAudio(string dir)
    {
        try
        {
            return System.IO.Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                .Any(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".wav" || AudioExtensions.Contains(ext);
                });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Not required at the set root: body clips usually live in body/.</summary>
    private void CheckSlot(PackHealth health, SetHealth set, string label, string slotName, string dir, bool required)
    {
        if (!System.IO.Directory.Exists(dir))
            return;

        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            Problem(health, label, slotName, $"could not be read: {e.Message}");
            return;
        }

        var wavs = new List<string>();
        var wrongFormat = new List<string>();

        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();

            if (ext == ".wav")
                wavs.Add(f);
            else if (AudioExtensions.Contains(ext))
                wrongFormat.Add(f);
        }

        if (wavs.Count == 0 && wrongFormat.Count == 0)
            return;

        var slot = new SlotHealth { Slot = slotName };

        foreach (var f in wavs)
        {
            var info = WavInspector.Inspect(f);
            var name = Path.GetFileName(f);

            if (!info.Ok)
            {
                slot.Bad++;
                Problem(health, label, $"{slotName}/{name}", info.Problem ?? "will not load.");
                continue;
            }

            slot.Clips++;

            if (info.Note is not null)
                Note(health, label, $"{slotName}/{name}", info.Note);
        }

        foreach (var f in wrongFormat)
        {
            var name = Path.GetFileName(f);
            slot.Bad++;

            Problem(health, label, $"{slotName}/{name}",
                "Resound only reads WAV, so this file is ignored.",
                $"ffmpeg -i \"{name}\" -ac 1 -ar 48000 -c:a pcm_s16le \"{Path.GetFileNameWithoutExtension(f)}.wav\"");
        }

        set.Slots.Add(slot);

        if (slot.Clips == 0 && required)
            Problem(health, label, slotName, "nothing here loads, so this slot keeps its vanilla sound.");
    }

    /// <summary>An orphaned set folder is usually a soundSet typo, and otherwise invisible.</summary>
    private void CheckUnusedSets(PackHealth health, string soundsRoot, HashSet<string> used)
    {
        string[] dirs;
        try
        {
            dirs = System.IO.Directory.GetDirectories(soundsRoot);
        }
        catch
        {
            return;
        }

        var orphans = dirs.Select(Path.GetFileName)
                          .Where(n => !string.IsNullOrEmpty(n) && !used.Contains(n))
                          .ToList();

        if (orphans.Count == 0)
            return;

        Problem(health, null, null,
            $"{orphans.Count} sound set folder(s) that no entry uses, so nothing in them is ever " +
            $"heard: {string.Join(", ", orphans)}",
            "Either add an entry with that soundSet, or delete the folder.");
    }

    // Case and separators are the usual slip, so say so outright.
    private static string? Nearest(string soundsRoot, string wanted)
    {
        string[] dirs;
        try
        {
            dirs = System.IO.Directory.GetDirectories(soundsRoot);
        }
        catch
        {
            return null;
        }

        var squashed = Squash(wanted);

        return dirs.Select(Path.GetFileName)
                   .FirstOrDefault(n => !string.IsNullOrEmpty(n) && Squash(n) == squashed);
    }

    private static string Squash(string s)
    {
        return s.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
    }

    private static string Label(PackRule rule)
    {
        if (!string.IsNullOrEmpty(rule.Name)) return rule.Name;
        if (!string.IsNullOrEmpty(rule.SoundSet)) return rule.SoundSet;
        return "(unnamed entry)";
    }

    private static void Problem(PackHealth h, string? entry, string? where, string message, string? fix = null)
    {
        h.Items.Add(new PackProblem { Entry = entry, Where = where, Message = message, Fix = fix });
    }

    private static void Note(PackHealth h, string? entry, string? where, string message)
    {
        h.Items.Add(new PackProblem { IsNote = true, Entry = entry, Where = where, Message = message });
    }
}
