using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GridPool.DashboardSimulator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<ISimulatorBroadcaster, SignalRSimulatorBroadcaster>();
builder.Services.AddSingleton<SimulatorEngine>();
builder.Services.AddHostedService<SimulatorTicker>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

WebApplication app = builder.Build();
string dashboardRoot = RequireDirectory("GRIDPOOL_SIM_DASHBOARD_ROOT");
string labRoot = RequireDirectory("GRIDPOOL_SIM_LAB_ROOT");
string simulatorKey = Environment.GetEnvironmentVariable("GRIDPOOL_SIM_OPERATOR_KEY")
    ?? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
IDeserializer yamlReader = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();
ISerializer yamlWriter = new SerializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
    .Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        await next();
    }
    catch (SimulatorApiException ex)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { status = "unavailable", reason = ex.Message });
    }
    catch (ArgumentException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { status = "rejected", reason = ex.Message });
    }
    catch (FormatException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { status = "rejected", reason = ex.Message });
    }
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/__sim") && !IsLoopback(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "rejected",
            reason = "Simulator controls are available only from the development machine."
        });
        return;
    }
    await next();
});

app.MapGet("/", () => Results.Redirect("/dashboard/"));
app.MapGet("/details", () => ServeSpaFile(dashboardRoot, null));
app.MapGet("/dashboard/{**path}", (string? path) =>
    ServeSpaFile(dashboardRoot, path));
app.MapGet("/__sim/{**path}", (HttpContext context, string? path) =>
    IsLoopback(context)
        ? ServeSpaFile(labRoot, path)
        : Results.StatusCode(StatusCodes.Status403Forbidden));

app.MapGet("/api/dashboard/v1/summary", (SimulatorEngine engine, string? window) =>
    Results.Ok(engine.Summary(window)));
app.MapGet("/api/dashboard/v1/history", (SimulatorEngine engine, string? window) =>
    Results.Ok(engine.History(window)));
app.MapGet("/api/dashboard/v1/address/{address}", (SimulatorEngine engine, string address) =>
    Results.Ok(engine.Address(address)));
app.MapGet("/api/dashboard/v1/operator", (HttpContext context, SimulatorEngine engine) =>
{
    string supplied = context.Request.Headers["X-Boot-Admin-Key"].FirstOrDefault() ?? string.Empty;
    return supplied == simulatorKey
        ? Results.Ok(engine.Operator())
        : Results.Unauthorized();
});
app.MapGet("/api/dashboard/v1/diagram", (SimulatorEngine engine) =>
    Results.Ok(engine.Diagram(operatorDetails: false)));
app.MapGet("/api/dashboard/v1/diagram/events", (SimulatorEngine engine, long? after, int? limit) =>
    Results.Ok(engine.DiagramEvents(after ?? 0, limit ?? 256, operatorDetails: false)));
app.MapGet("/api/dashboard/v1/diagram/operator", (HttpContext context, SimulatorEngine engine) =>
{
    string supplied = context.Request.Headers["X-Boot-Admin-Key"].FirstOrDefault() ?? string.Empty;
    return supplied == simulatorKey
        ? Results.Ok(engine.Diagram(operatorDetails: true))
        : Results.Unauthorized();
});
app.MapGet("/api/dashboard/v1/diagram/operator/events", (
    HttpContext context,
    SimulatorEngine engine,
    long? after,
    int? limit) =>
{
    string supplied = context.Request.Headers["X-Boot-Admin-Key"].FirstOrDefault() ?? string.Empty;
    return supplied == simulatorKey
        ? Results.Ok(engine.DiagramEvents(after ?? 0, limit ?? 256, operatorDetails: true))
        : Results.Unauthorized();
});
app.MapGet("/api/dashboard/v1/schema", () => Results.Ok(new
{
    schemaVersion = 1,
    simulator = true,
    endpoints = new
    {
        summary = "/api/dashboard/v1/summary?window=24h",
        history = "/api/dashboard/v1/history?window=24h",
        address = "/api/dashboard/v1/address/{address}",
        @operator = "/api/dashboard/v1/operator",
        diagram = "/api/dashboard/v1/diagram",
        diagramEvents = "/api/dashboard/v1/diagram/events?after={sequence}",
        operatorDiagram = "/api/dashboard/v1/diagram/operator",
        operatorDiagramEvents = "/api/dashboard/v1/diagram/operator/events?after={sequence}"
    },
    windows = new[] { "6h", "24h", "7d" },
    realtime = new { hub = "/dashboardHub", method = "DashboardChanged" },
    authentication = new
    {
        operatorHeader = "X-Boot-Admin-Key",
        simulatorKeyHint = "The local launcher prints the synthetic key."
    }
}));
app.MapHub<SimulatorDashboardHub>("/dashboardHub");

RouteGroupBuilder controls = app.MapGroup("/__sim/api/v1");
controls.AddEndpointFilter(async (context, next) =>
{
    HttpContext http = context.HttpContext;
    if (!IsLoopback(http))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
    return await next(context);
});

controls.MapGet("/state", (SimulatorEngine engine) => Results.Ok(engine.Read()));
controls.MapPut("/state", async (SimulatorState state, SimulatorEngine engine) =>
{
    await engine.ReplaceAsync(state);
    return Results.Ok(engine.Read());
});
controls.MapPost("/actions", async (SimulatorAction action, SimulatorEngine engine) =>
{
    await engine.ApplyAsync(action);
    return Results.Ok(engine.Read());
});
controls.MapGet("/scenarios", () => Results.Ok(SimulatorScenarios.All));
controls.MapPost("/scenarios/{id}/load", async (string id, SimulatorEngine engine) =>
{
    await engine.LoadScenarioAsync(id);
    return Results.Ok(engine.Read());
});
controls.MapGet("/events", (SimulatorEngine engine) => Results.Ok(engine.Read().Events));
controls.MapPost("/reset", async (SimulatorEngine engine) =>
{
    SimulatorState current = engine.Read();
    await engine.ReplaceAsync(SimulatorScenarios.Create(current.Scenario, current.Seed), "reset");
    return Results.Ok(engine.Read());
});
controls.MapPost("/timeline/play", async (SimulatorEngine engine) =>
{
    await engine.SetPlayingAsync(true);
    return Results.Ok(engine.Read());
});
controls.MapPost("/timeline/pause", async (SimulatorEngine engine) =>
{
    await engine.SetPlayingAsync(false);
    return Results.Ok(engine.Read());
});
controls.MapPost("/timeline/step", async (SimulatorEngine engine) =>
{
    await engine.StepTimelineAsync();
    return Results.Ok(engine.Read());
});
controls.MapPost("/timeline/reset", async (SimulatorEngine engine) =>
{
    await engine.ResetTimelineAsync();
    return Results.Ok(engine.Read());
});
controls.MapPost("/import", async (HttpContext context, SimulatorEngine engine) =>
{
    using StreamReader reader = new(context.Request.Body);
    string body = await reader.ReadToEndAsync();
    TimelineDocument timeline = yamlReader.Deserialize<TimelineDocument>(body)
        ?? throw new FormatException("The YAML document was empty.");
    await engine.SetTimelineAsync(timeline);
    return Results.Ok(engine.Read());
});
controls.MapGet("/export", (SimulatorEngine engine) =>
{
    SimulatorState state = engine.Read();
    DateTime baseTime = state.Events.Count > 0 ? state.Events[0].TimestampUtc : state.VirtualTimeUtc;
    TimelineDocument timeline = state.Timeline ?? new TimelineDocument
    {
        Name = $"manual-{state.Scenario}",
        Seed = state.Seed,
        InitialScenario = state.Scenario,
        Events = state.Events.Select(item => new TimelineEvent
        {
            At = $"{Math.Max(0, (item.TimestampUtc - baseTime).TotalSeconds):0.###}s",
            Action = item.Action,
            Peer = item.Arguments.GetValueOrDefault("peer"),
            Adapter = item.Arguments.GetValueOrDefault("adapter"),
            Miner = item.Arguments.GetValueOrDefault("miner"),
            Address = item.Arguments.GetValueOrDefault("address"),
            Transport = item.Arguments.GetValueOrDefault("transport"),
            Value = double.TryParse(item.Arguments.GetValueOrDefault("value"), out double value) ? value : null,
            Count = int.TryParse(item.Arguments.GetValueOrDefault("count"), out int count) ? count : null,
            Rank = int.TryParse(item.Arguments.GetValueOrDefault("rank"), out int rank) ? rank : null
        }).ToList()
    };
    return Results.Text(yamlWriter.Serialize(timeline), "application/yaml");
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    string[] urls = app.Urls.ToArray();
    Console.WriteLine("GridPool dashboard simulator is serving synthetic data only.");
    Console.WriteLine($"Synthetic operator key: {simulatorKey}");
    foreach (string url in urls)
    {
        Console.WriteLine($"Dashboard: {url}/dashboard/");
        Console.WriteLine($"Controls:  {url}/__sim/");
    }
});

app.Run();

static bool IsLoopback(HttpContext context)
{
    return SimulatorAccess.IsLoopback(context.Connection.RemoteIpAddress);
}

static string RequireDirectory(string name)
{
    string? path = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
    {
        throw new InvalidOperationException(
            $"{name} must point to an existing build directory. Use scripts/run-dashboard-lab.sh.");
    }
    return Path.GetFullPath(path);
}

static IResult ServeSpaFile(string root, string? requestedPath)
{
    string relative = string.IsNullOrWhiteSpace(requestedPath) ? "index.html" : requestedPath;
    string candidate = Path.GetFullPath(Path.Combine(root, relative));
    string normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    if (!candidate.StartsWith(normalizedRoot, StringComparison.Ordinal) || !File.Exists(candidate))
    {
        candidate = Path.Combine(root, "index.html");
    }

    FileExtensionContentTypeProvider types = new();
    string contentType = types.TryGetContentType(candidate, out string? resolved)
        ? resolved
        : "application/octet-stream";
    return Results.File(candidate, contentType, enableRangeProcessing: true);
}

public partial class Program;

namespace GridPool.DashboardSimulator
{
    public static class SimulatorAccess
    {
        public static bool IsLoopback(IPAddress? address) =>
            address != null &&
            (IPAddress.IsLoopback(address) ||
             address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4()));
    }

    public interface ISimulatorBroadcaster
    {
        Task BroadcastAsync(boot_portal.Models.DashboardChangedDto change);
    }

    public sealed class SignalRSimulatorBroadcaster(
        IHubContext<SimulatorDashboardHub> hub) : ISimulatorBroadcaster
    {
        public Task BroadcastAsync(boot_portal.Models.DashboardChangedDto change) =>
            hub.Clients.All.SendAsync("DashboardChanged", change);
    }

    public sealed class SimulatorDashboardHub(SimulatorEngine engine) : Hub
    {
        public Task<long> GetRevision() => Task.FromResult(engine.Revision);
    }
}
