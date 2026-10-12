using ResoundServer.Models;
using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>The builder's "will this be heard" answer must use the game's own order.</summary>
public class OutranksTests
{
    private static PackRow Held(string id, int priority) => new() { Id = id, Priority = priority };

    private static PackRule By(int specificity)
    {
        // Real match keys rather than numbers, so these survive a change to the weights.
        return specificity switch
        {
            100 => new PackRule { TemplateId = "abc" },
            40 => new PackRule { WeapClass = "smg" },
            70 => new PackRule { WeapClass = "smg", Caliber = "Caliber9x19PARA" },
            20 => new PackRule { NamePattern = "ak" },
            _ => throw new ArgumentOutOfRangeException(nameof(specificity))
        };
    }

    [Fact]
    public void Higher_priority_wins_whatever_the_specificity()
    {
        Assert.True(ResoundService.Outranks(10, 20, "mine", Held("theirs", 0), By(100)));
    }

    [Fact]
    public void Lower_priority_loses_whatever_the_specificity()
    {
        Assert.False(ResoundService.Outranks(-50, 100, "mine", Held("theirs", 0), By(40)));
    }

    [Fact]
    public void At_the_same_priority_the_more_specific_entry_wins()
    {
        Assert.True(ResoundService.Outranks(0, 100, "mine", Held("theirs", 0), By(40)));
        Assert.False(ResoundService.Outranks(0, 40, "mine", Held("theirs", 0), By(100)));
    }

    [Fact]
    public void A_class_plus_calibre_beats_a_class_alone()
    {
        Assert.True(ResoundService.Outranks(0, By(70).Specificity, "mine", Held("theirs", 0), By(40)));
    }

    [Fact]
    public void A_dead_heat_is_broken_by_pack_id_so_it_lands_the_same_way_everywhere()
    {
        Assert.True(ResoundService.Outranks(0, 40, "aaa", Held("zzz", 0), By(40)));
        Assert.False(ResoundService.Outranks(0, 40, "zzz", Held("aaa", 0), By(40)));
    }

    [Fact]
    public void A_pack_never_outranks_itself()
    {
        Assert.False(ResoundService.Outranks(0, 40, "mine", Held("mine", 0), By(40)));
    }

    [Fact]
    public void Anything_outranks_the_sounds_shipped_with_resound()
    {
        // The built-in floor: an installed pack wins even with a vaguer match.
        var builtin = Held(ResoundService.BuiltinId, -100);

        Assert.True(ResoundService.Outranks(0, 20, "mine", builtin, By(100)));
        Assert.True(ResoundService.Outranks(-50, 20, "mine", builtin, By(100)));
    }
}
