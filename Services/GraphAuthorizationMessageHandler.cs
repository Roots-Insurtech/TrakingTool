using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace TrakingTool.Services;

public sealed class GraphAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public GraphAuthorizationMessageHandler(IAccessTokenProvider provider, NavigationManager navigation)
        : base(provider, navigation)
    {
        ConfigureHandler(
            authorizedUrls: new[] { "https://graph.microsoft.com" },
            scopes: new[] { "User.Read", "Files.ReadWrite.AppFolder" });
    }
}
