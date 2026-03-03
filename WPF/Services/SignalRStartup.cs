using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Cors;
using Owin;

namespace SNMPSimMgr.Services;

public class SignalRStartup
{
    public void Configuration(IAppBuilder app)
    {
        // Enable CORS for Angular dev server and any other origin
        app.UseCors(CorsOptions.AllowAll);

        // Configure SignalR
        var hubConfig = new HubConfiguration
        {
            EnableDetailedErrors = true,
            EnableJSONP = false
        };

        // NOTE: Do NOT set a global CamelCasePropertyNamesContractResolver here.
        // SignalR 2.0 uses PascalCase for its internal protocol messages (negotiate,
        // start, etc.) and the JS client expects PascalCase. Setting camelCase globally
        // breaks the protocol handshake ("server version undefined" error).
        // Hub method return values will use Newtonsoft.Json default (PascalCase) —
        // the Angular client handles property mapping.

        app.MapSignalR(hubConfig);
    }
}
