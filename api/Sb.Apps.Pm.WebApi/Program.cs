using Sb.Apps.Pm.Services;
using Sb.Apps.Pm.WebApi.Agent;
using Sb.Apps.Pm.WebApi.WebTerminals;
using SB.Framework.WebApi.Extensions;
using SB.Framework.WebApi.Middleware;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.AddSbFramework();

bool isInstalled = File.Exists(Path.Combine(
    builder.Environment.ContentRootPath, "installed.lock"));
if (isInstalled)
{
    builder.Services.PostConfigure<SwaggerGenOptions>(AgentSwaggerSetup.AddAgentSwaggerDoc);
}

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(120) });
app.UseWebTerminalEndpoint();
app.UseSbFramework();
await app.RunAsync();
