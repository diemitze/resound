using ResoundServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace ResoundServer.Routers;

/// <summary>/resound/config returns the saved picks, /resound/resolve the decision table. Built per request.</summary>
[Injectable(TypePriority = OnLoadOrder.Preload)]
public class ConfigRoutes : StaticRouter
{
    // StaticRouter wants its routes before instance state exists, so the service arrives through a field.
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
            async (url, info, sessionId, output, cancellationToken) =>
                await new ValueTask<string>(_json!.Serialize(payload()) ?? "{}"));
    }
}
