using ResoundServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace ResoundServer.Routers;


[Injectable(TypePriority = OnLoadOrder.PreSptModLoader)]
public class ConfigRoutes : StaticRouter
{
   
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
