using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using TrakingTool;
using TrakingTool.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.DefaultAccessTokenScopes.Add("User.Read");
    options.ProviderOptions.DefaultAccessTokenScopes.Add("Files.ReadWrite.AppFolder");
    options.ProviderOptions.LoginMode = "redirect";
});

builder.Services.AddScoped<GraphAuthorizationMessageHandler>();
builder.Services.AddHttpClient("graph", client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Graph:BaseUrl"] ?? "https://graph.microsoft.com/v1.0/");
    })
    .AddHttpMessageHandler<GraphAuthorizationMessageHandler>();

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
    config.SnackbarConfiguration.VisibleStateDuration = 3000;
});

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped<LocalEntryRepository>();
builder.Services.AddScoped<OneDriveEntryRepository>();
builder.Services.AddScoped<PendingSyncStore>();
builder.Services.AddScoped<SyncStateService>();
builder.Services.AddScoped<OnlineStatusService>();
builder.Services.AddScoped<IEntryRepository, CachedEntryRepository>();
builder.Services.AddScoped(sp => (CachedEntryRepository)sp.GetRequiredService<IEntryRepository>());

await builder.Build().RunAsync();
