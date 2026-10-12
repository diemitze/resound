using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;

namespace ResoundServer;

// Without IModBlazorMetadata the assembly loads but the pages under Web/ never appear.
public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.20fpsguy.resound.server";
    public string Name { get; init; } = "Resound Config";
    public string Author { get; init; } = "20fpsguy";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.2.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/diemitze/resound";
    public bool HasPrepatcher { get; init; }
    public string License { get; init; } = "MIT";

    // IModBlazorMetadata: where the server's mod page links to.
    public string? HomePage { get; init; } = "/resound";
    public string? HomePageDescription { get; init; } = "Pick a sound pack per weapon";
    public string? WWWRootUrl { get; init; } = "";
}
