using System.IO.Compression;
using ResoundServer.Models;
using ResoundServer.Services;

namespace ResoundServer.Tests;

/// <summary>The exported zip must carry BepInEx/plugins/Resound/id/ inside it.</summary>
public class PackExportTests : IDisposable
{
    private readonly TempPack _temp = new();
    private readonly PackBuilderService _builder;

    public PackExportTests()
    {
        _builder = new PackBuilderService(new PackValidator(), new TestLogger<PackBuilderService>())
        {
            InstallRoot = _temp.At("plugins", "Resound")
        };
    }

    public void Dispose() => _temp.Dispose();

    private BuildResult Built()
    {
        var set = new SetDraft { Rule = new PackRule { Name = "SMG", SoundSet = "mysmg", WeapClass = "smg" } };

        set.Files =
        [
            new StagedFile { Slot = "body", SourcePath = _temp.Wav("rips/shot.wav") },
            new StagedFile { Slot = "tail", SourcePath = _temp.Wav("rips/dist.wav") }
        ];

        return _builder.Build(new PackDraft
        {
            Id = "mypack", Name = "My Pack", Author = "me", Sets = [set]
        });
    }

    private static List<string> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
    }

    [Fact]
    public void The_zip_carries_the_install_path_so_extracting_it_is_the_whole_install()
    {
        var zip = _builder.Export(Built().Directory);

        Assert.True(File.Exists(zip));
        Assert.All(Entries(zip), e => Assert.StartsWith("BepInEx/plugins/Resound/mypack/", e));
    }

    [Fact]
    public void The_manifest_and_the_audio_are_both_in_it()
    {
        var entries = Entries(_builder.Export(Built().Directory));

        Assert.Contains("BepInEx/plugins/Resound/mypack/resound.pack.json", entries);
        Assert.Contains("BepInEx/plugins/Resound/mypack/sounds/mysmg/body/shot.wav", entries);
        Assert.Contains("BepInEx/plugins/Resound/mypack/sounds/mysmg/tail/dist.wav", entries);
    }

    [Fact]
    public void Paths_inside_the_zip_use_forward_slashes_whatever_the_machine_uses()
    {
        Assert.All(Entries(_builder.Export(Built().Directory)), e => Assert.DoesNotContain("\\", e));
    }

    [Fact]
    public void It_lands_next_to_the_pack_and_is_named_after_it()
    {
        var zip = _builder.Export(Built().Directory);

        Assert.Equal("mypack.zip", Path.GetFileName(zip));
        Assert.Equal(_temp.At("plugins", "Resound"), Path.GetDirectoryName(zip));
    }

    [Fact]
    public void Exporting_twice_replaces_the_zip_rather_than_doubling_everything_in_it()
    {
        var built = Built();

        var first = Entries(_builder.Export(built.Directory));
        var second = Entries(_builder.Export(built.Directory));

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_zip_can_be_written_somewhere_else_when_asked()
    {
        var destination = _temp.At("elsewhere", "handout.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        Assert.Equal(destination, _builder.Export(Built().Directory, destination));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void Exporting_something_that_is_not_there_says_so_rather_than_writing_an_empty_zip()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _builder.Export(_temp.At("nope")));
    }

    [Fact]
    public void What_comes_out_of_the_zip_is_a_pack_that_passes_its_own_health_check()
    {
        var zip = _builder.Export(Built().Directory);
        var unpacked = _temp.At("unpacked");

        ZipFile.ExtractToDirectory(zip, unpacked);

        var landed = Path.Join(unpacked, "BepInEx", "plugins", "Resound", "mypack");
        var pack = PackManifest.Read(Path.Join(landed, PackManifest.FileName));

        Assert.Equal("mypack", pack.Id);
        Assert.True(new PackValidator().Check(pack).Healthy);
    }
}
