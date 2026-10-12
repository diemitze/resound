using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ResoundServer.Services;
using ResoundServer.Web.Pages;

namespace ResoundServer.Tests;

/// <summary>Drop an mp3 and it works. Needs a real ffmpeg and skips without one.</summary>
public class BuildPageWithFfmpegTests : BunitContext
{
    private readonly TempPack _temp = new();
    private readonly AudioConverter _converter = new(new TestLogger<AudioConverter>());

    public BuildPageWithFfmpegTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(FakeSpt.Service());
        Services.AddSingleton(_converter);
        Services.AddSingleton(new PackValidator());
        Services.AddSingleton(sp => new PackStaging(_converter, new TestLogger<PackStaging>())
        {
            Root = _temp.At("staging")
        });
        Services.AddSingleton(sp => new PackBuilderService(
            sp.GetRequiredService<PackValidator>(), new TestLogger<PackBuilderService>())
        {
            InstallRoot = _temp.At("plugins", "Resound")
        });

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void An_mp3_dropped_on_a_set_is_converted_and_ends_up_in_the_pack()
    {
        if (!_converter.HasFfmpeg) Assert.Skip("ffmpeg is not installed.");

        Mp3("rips/wpn_ak_shot_plr.mp3");

        var page = Render<Build>();

        Field(page, "What is your pack called?").Input("mypack");
        Click(page, "Add a sound set");
        Field(page, "Folder of rips").Input(_temp.At("rips"));
        Click(page, "Scan");

        // The page says what it did to the file rather than quietly swapping it.
        Assert.Contains("converted to mono 48kHz", page.Markup);
        Assert.Contains("<b>1</b> clip", page.Markup);
        Assert.DoesNotContain("only reads WAV", page.Markup);

        Click(page, "Build the pack");

        var shipped = Path.Join(_temp.At("plugins", "Resound"), "mypack",
            "sounds", "assaultrifle", "body", "wpn_ak_shot_plr.wav");

        Assert.True(File.Exists(shipped));

        var info = WavInspector.Inspect(shipped);
        Assert.True(info.Ok);
        Assert.Equal(1, info.Channels);
        Assert.Equal(48000, info.SampleRate);
    }

    [Fact]
    public void With_ffmpeg_present_the_page_does_not_nag_about_installing_it()
    {
        if (!_converter.HasFfmpeg) Assert.Skip("ffmpeg is not installed.");

        var page = Render<Build>();
        Click(page, "Add a sound set");

        Assert.DoesNotContain("WAV only on this machine", page.Markup);
    }

    private void Mp3(string relativePath)
    {
        var path = _temp.At(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _converter.FfmpegPath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        foreach (var a in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y",
                     "-f", "lavfi", "-i", "sine=frequency=800:duration=0.4", "-ac", "2", path
                 })
            start.ArgumentList.Add(a);

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<Build> page, string label)
    {
        var id = page.FindAll("label").First(l => l.TextContent.Trim() == label).GetAttribute("for");
        return page.FindAll("input").First(i => i.GetAttribute("id") == id);
    }

    private static void Click(IRenderedComponent<Build> page, string text)
    {
        page.FindAll("button").First(b => b.TextContent.Contains(text)).Click();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _temp.Dispose();
        }

        base.Dispose(disposing);
    }
}
