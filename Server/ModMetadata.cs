using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;

namespace ResoundServer;

// Every SPT server mod declares itself this way; the abstract base fixes which members exist.
// The part that matters here is IModWebMetadata, which is what makes the server pick up the
// Blazor pages under Web/. Without it the assembly still loads and the routes simply never appear.
public record ModMetadata : AbstractModMetadata, IModWebMetadata
{
    public override string ModGuid { get; init; } = "com.20fpsguy.resound.server";
    public override string Name { get; init; } = "Resound Config";
    public override string Author { get; init; } = "20fpsguy";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/diemitze/resound";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
