using ResoundServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace ResoundServer.Routers;

/// <summary>
/// The two endpoints the client plugin reads at startup. <c>/resound/config</c> returns the raw
/// saved selection; <c>/resound/resolve</c> returns the decision table, every weapon template id
/// mapped to the pack and sound set that wins for it. The client applies that answer rather than
/// working it out again, so the config page can never show something the game disagrees with.
/// Both are built per request, so saving on the page takes effect the next time a client starts.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PreSptModLoader)]
public class ConfigRoutes : StaticRouter
{
    // StaticRouter wants its routes at construction, before instance state exists, so the
    // service is handed to the route bodies through a field the constructor fills.
    private static ResoundService? _service;
    private static JsonUtil? _json;

    public ConfigRoutes(JsonUtil json, ResoundService service)
        : base(json, BuildRoutes())
    {
        _json = json;
        _service = service;
    }

    private static List<RouteAction> BuildRoutes()
    {
        return
        [
            Route("/resound/config", () => _service!.Config),
            Route("/resound/resolve", () => _service!.BuildResolutionMap())
        ];
    }

    private static RouteAction Route(string path, Func<object> payload)
    {
        return new RouteAction(path,
            async (url, info, sessionId, output) =>
                await new ValueTask<string>(_json!.Serialize(payload()) ?? "{}"));
    }
}
