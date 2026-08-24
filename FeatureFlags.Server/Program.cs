using FeatureFlags.Infrastructure;
using FeatureFlags.Server.Api;
using FeatureFlags.Server.Features.Flags.CreateFlag;
using FeatureFlags.Server.Evaluation;
using FeatureFlags.Server.Features.Evaluation.EvaluateForContext;
using FeatureFlags.Server.Features.Evaluation.GetRuleset;
using FeatureFlags.Server.Features.Flags.EvaluateFlags;
using FeatureFlags.Server.Features.Flags.GetFlag;
using FeatureFlags.Server.Features.Flags.GetFlagHistory;
using FeatureFlags.Server.Features.Flags.ListFlags;
using FeatureFlags.Server.Features.Flags.SetFlagTargeting;
using FeatureFlags.Server.Features.Flags.ToggleFlag;
using FeatureFlags.Server.Features.Flags.UpdateFlag;
using FeatureFlags.Server.Features.SdkKeys.IssueSdkKey;
using FeatureFlags.Server.Features.SdkKeys.ListSdkKeys;
using FeatureFlags.Server.Features.SdkKeys.RevokeSdkKey;
using FeatureFlags.Server.Features.Segments.CreateSegment;
using FeatureFlags.Server.Features.Segments.DeleteSegment;
using FeatureFlags.Server.Features.Segments.GetSegment;
using FeatureFlags.Server.Features.Segments.GetSegmentHistory;
using FeatureFlags.Server.Features.Segments.ListSegments;
using FeatureFlags.Server.Features.Segments.UpdateSegment;
using FeatureFlags.Server.Features.Users.GetCurrentUser;
using FeatureFlags.Server.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Before anything reads configuration: outside the AppHost the connection strings and the auth
// service's address arrive under documented FEATUREFLAGS_* names instead of Aspire's keys.
builder.AddSelfHostConfiguration();

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
// The distributed cache is what HybridCache uses as its second tier, behind an in-process one.
// It backs the evaluation endpoint — the only route a fleet of application servers polls, and so
// the only one where the same answer is worth computing once for everybody.
builder.AddRedisClientBuilder("cache")
    .WithOutputCache()
    .WithDistributedCache();

builder.Services.AddHybridCache();
builder.AddInfrastructure();
builder.AddConsoleAuthentication();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddBrowserCors(builder.Configuration);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

// Better Auth runs in its own Node process. Forwarding to it from here rather than exposing it
// directly is what keeps the console on one origin, which is what keeps its session cookie
// first-party — see the /api/auth route below.
builder.Services.AddHttpForwarder();

// Feature slice handlers.
builder.Services.AddScoped<CreateFlagHandler>();
builder.Services.AddScoped<ListFlagsHandler>();
builder.Services.AddScoped<ToggleFlagHandler>();
builder.Services.AddScoped<EvaluateFlagsHandler>();
builder.Services.AddScoped<RulesetProvider>();
builder.Services.AddScoped<GetRulesetHandler>();
builder.Services.AddScoped<EvaluateForContextHandler>();
builder.Services.AddScoped<GetFlagHandler>();
builder.Services.AddScoped<UpdateFlagHandler>();
builder.Services.AddScoped<GetFlagHistoryHandler>();
builder.Services.AddScoped<SetFlagTargetingHandler>();
builder.Services.AddScoped<CreateSegmentHandler>();
builder.Services.AddScoped<ListSegmentsHandler>();
builder.Services.AddScoped<GetSegmentHandler>();
builder.Services.AddScoped<UpdateSegmentHandler>();
builder.Services.AddScoped<DeleteSegmentHandler>();
builder.Services.AddScoped<GetSegmentHistoryHandler>();
builder.Services.AddScoped<IssueSdkKeyHandler>();
builder.Services.AddScoped<ListSdkKeysHandler>();
builder.Services.AddScoped<RevokeSdkKeyHandler>();
builder.Services.AddScoped<GetCurrentUserHandler>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Deployed, the console is reached through a TLS-terminating proxy — Caddy in the compose bundle,
// an ingress controller in the chart. Without this the server believes every request is plain HTTP
// and hands out http:// Location headers on the https:// origin it is actually serving.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Only the nearest hop is honoured, so a client cannot prepend its own entry and have it
    // survive. Explicit because it is the security property this depends on, not because the
    // default differs.
    options.ForwardLimit = 1;

    // The proxy is a neighbouring container or pod on an address assigned at run time, so there is
    // no known-proxy list to write and the default one would reject it. What bounds the trust is
    // the network rather than the header: both deployment artifacts keep the server unpublished
    // behind the proxy, so nothing else can reach this port to spoof anything. Publishing the
    // server's port directly breaks that assumption — do not.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Configuration.GetConsoleOrigin() is not null)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Configuration.ShouldApplyMigrations(app.Environment))
{
    await app.Services.ApplyMigrationsAsync();
}

// Migrating is the whole job here, so stop rather than go on to serve. An unhandled exception
// above has already left through Main by this point, which is what makes a failed migration a
// non-zero exit and so a failed deployment.
if (app.Configuration.IsMigrateOnly())
{
    return;
}

app.UseOutputCache();

app.UseRouting();

// Between authentication and authorization: after UseRouting so the endpoint's RequireCors
// metadata has been resolved, and before the endpoint runs so a preflight is answered without
// reaching it. A preflight carries no credential, which is why the key-kind rule lives in the
// endpoint rather than here — see EvaluateFlagsEndpoint.
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

// Everything Better Auth owns — sign-in, sign-up, sessions, tokens, the JWKS — is answered by
// the auth service. The console never calls it directly: on this origin the session cookie is
// first-party, and in development Vite's proxy already sends /api here.
app.MapForwarder("/api/auth/{**catch-all}", app.Configuration.GetAuthServiceAddress())
    .WithName("ForwardToAuthService");

api.MapListFlags();
api.MapCreateFlag();
api.MapToggleFlag();
api.MapEvaluateFlags();
api.MapGetRuleset();
api.MapEvaluateForContext();
api.MapGetFlag();
api.MapUpdateFlag();
api.MapGetFlagHistory();
api.MapSetFlagTargeting();
api.MapListSegments();
api.MapCreateSegment();
api.MapGetSegment();
api.MapUpdateSegment();
api.MapDeleteSegment();
api.MapGetSegmentHistory();
api.MapIssueSdkKey();
api.MapListSdkKeys();
api.MapRevokeSdkKey();
api.MapGetCurrentUser();

app.MapDefaultEndpoints();

app.UseFileServer();

// An unmatched /api route is a caller's mistake, not a console route. Claim it
// here — the literal segment outranks the catch-all below at the same order — so
// a bad API call gets a 404 instead of a 200 full of HTML.
api.MapFallback(() => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "Not found",
    detail: "No API endpoint matches this route."));

// The console is a single-page app: every remaining client route has to be
// answered with its shell so deep links survive a reload.
app.MapFallbackToFile("index.html");

app.Run();
