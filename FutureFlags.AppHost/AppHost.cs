var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
var futureFlagsDb = postgres.AddDatabase("futureflagsdb");

var authSecret = builder.AddParameter("auth-secret", secret: true);

// Better Auth is a Node library, so it needs a Node process of its own — the console is
// static files in production and cannot host it. It shares the application's database
// but keeps its tables in the `auth` schema, and applies its own migrations at startup.
// Published as a package script rather than a static build: unlike the console, this
// resource is a running server and needs its node_modules at runtime.
#pragma warning disable ASPIREJAVASCRIPT001 // PublishAsPackageScript is for evaluation only.
var auth = builder.AddJavaScriptApp("auth", "../auth")
    .WithPnpm()
    .WithHttpEndpoint(env: "PORT")
    .WithReference(futureFlagsDb)
    .WaitFor(futureFlagsDb)
    .WithEnvironment("BETTER_AUTH_SECRET", authSecret)
    .WithHttpHealthCheck("/health")
    .PublishAsPackageScript(scriptName: "start");
#pragma warning restore ASPIREJAVASCRIPT001

// The browser only ever reaches the auth service through the server's /api/auth
// forwarder, so the origin it has to trust is the console's rather than its own.
// Referencing that endpoint here would close a cycle (frontend → server → auth), so the
// value arrives as configuration: a wildcard covering whichever port Vite takes locally,
// and the console's real origin once deployed.
if (builder.ExecutionContext.IsPublishMode)
{
    var consoleOrigin = builder.AddParameter("console-origin");

    auth.WithEnvironment("BETTER_AUTH_URL", consoleOrigin)
        .WithEnvironment("BETTER_AUTH_TRUSTED_ORIGINS", consoleOrigin);
}
else
{
    // Aspire serves each resource from a `<resource>-<app>.dev.localhost` hostname, which is
    // what a browser actually loads — the bare localhost URLs are internal. Both shapes are
    // trusted here because either can be the origin depending on how the console was opened.
    auth.WithEnvironment(
        "BETTER_AUTH_TRUSTED_ORIGINS",
        string.Join(',', [
            "http://localhost:*",
            "https://localhost:*",
            "http://127.0.0.1:*",
            "https://127.0.0.1:*",
            "http://*.localhost:*",
            "https://*.localhost:*"
        ]));
}

var server = builder.AddProject<Projects.FutureFlags_Server>("server")
    .WithReference(cache)
    .WithReference(futureFlagsDb)
    .WithReference(auth)
    .WaitFor(cache)
    .WaitFor(futureFlagsDb)
    // Not only so the forwarder has something to forward to: the server's migration puts
    // a trigger on a table Better Auth owns, so that table has to exist first.
    .WaitFor(auth)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithPnpm()
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
