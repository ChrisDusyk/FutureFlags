using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace FutureFlags.Server.Hosting;

/// <summary>
/// Translates the documented <c>FUTUREFLAGS_*</c> environment variables a self-hosting operator
/// sets into the configuration keys this application actually reads.
///
/// Those keys are Aspire's shape — <c>ConnectionStrings:futureflagsdb</c>, <c>services:auth:http:0</c>
/// — and they are an implementation detail of how the AppHost wires resources together. Nobody
/// running a container should have to learn them, and building the deployment surface on top of
/// them would make an Aspire convention change a breaking change for every consumer.
///
/// Nothing here overwrites a key that is already set, so the AppHost is unaffected: Aspire's
/// injected values are in configuration by the time this runs, and they win.
/// </summary>
public static class SelfHostConfiguration
{
    /// <summary>The origin a browser sees, e.g. <c>https://flags.example.com</c>.</summary>
    public const string OriginVariable = "FUTUREFLAGS_ORIGIN";

    public const string DatabaseUrlVariable = "FUTUREFLAGS_DATABASE_URL";
    public const string RedisUrlVariable = "FUTUREFLAGS_REDIS_URL";
    public const string AuthUrlVariable = "FUTUREFLAGS_AUTH_URL";
    public const string ApplyMigrationsVariable = "FUTUREFLAGS_APPLY_MIGRATIONS";
    public const string MigrateOnlyVariable = "FUTUREFLAGS_MIGRATE_ONLY";

    /// <summary>
    /// Comma-separated origins of web applications that read flags from a browser. Empty unless a
    /// self-hoster has one, which is the right default — an installation read only by server-side
    /// code should not answer a cross-origin request at all.
    /// </summary>
    public const string BrowserOriginsVariable = "FUTUREFLAGS_BROWSER_ORIGINS";

    private const string DatabaseConnectionStringKey = "ConnectionStrings:futureflagsdb";
    private const string CacheConnectionStringKey = "ConnectionStrings:cache";
    private const string AuthServiceAddressKey = "services:auth:http:0";

    private const int DefaultPostgresPort = 5432;
    private const int DefaultRedisPort = 6379;

    private const string AuthUrlIsNotAnAddress =
        $"{AuthUrlVariable} has to be an absolute http:// or https:// address, e.g. " +
        "http://auth:8080. It is the auth service's address inside your network, and both the " +
        "/api/auth forwarder and the JWKS lookup are built from it — without a scheme neither " +
        "can dial anything, and the first sign-in is where that would otherwise be discovered.";

    private const string AuthUrlCarriesMoreThanAnAddress =
        $"{AuthUrlVariable} carries a query or a fragment, and it is a base address: the JWKS " +
        "path and every forwarded request are appended to it, which would put them after the " +
        "query rather than before it. Give it a scheme, a host, and a port. A path is fine — the " +
        "forwarder and the JWKS lookup both build on it.";

    private const string OriginIsNotAnOrigin =
        $"{OriginVariable} has to start with https:// or http://, e.g. https://flags.example.com. " +
        "It is the origin a browser sends, not a hostname — the auth service compares it against " +
        "the Origin header on every sign-in, and a value that is not an origin matches nothing.";

    private const string OriginCarriesMoreThanAnOrigin =
        $"{OriginVariable} has to be a scheme, a host, and an optional port, with nothing after " +
        "it. A browser never puts a path, a query, or a fragment in an Origin header, so this " +
        "would be refused at the first sign-in with an error naming only the origin. If the " +
        "console has to live under a path, that belongs in the proxy in front of this, not here.";

    private const string UnparseableDatabaseUrl =
        $"{DatabaseUrlVariable} is not a valid postgres:// URL — the scheme is not the problem, " +
        "postgresql:// is equally accepted. The usual cause is an unescaped " +
        "character in the password — '/', '@', ':' and '#' each have a meaning in a URL and have " +
        "to be percent-encoded ('/' as %2F, '@' as %40). Generating the password with " +
        "`openssl rand -hex 24` avoids the problem; `-base64` does not, because base64 contains '/'.";

    private const string DatabaseUrlNamesNoDatabase =
        $"{DatabaseUrlVariable} names no database — there is nothing after the host. Write it as " +
        "postgres://user:password@host:5432/futureflagsdb. Left out, the driver connects to a " +
        "database named after the user instead, which is either missing or somebody else's.";

    private const string DatabaseUrlIsNotAUrl =
        $"{DatabaseUrlVariable} has to be a postgres:// or postgresql:// URL, e.g. " +
        "postgres://user:password@host:5432/futureflagsdb. The auth service reads this same " +
        "variable and parses it as a URL, so anything else configures half the stack and " +
        "crashloops the other half. Npgsql's own settings work here as query parameters " +
        "(?sslmode=require). If a native Npgsql connection string is genuinely needed, set " +
        "ConnectionStrings__futureflagsdb directly — that one is read by this service alone, " +
        "and the auth service then has to be pointed at the database separately.";

    public static TBuilder AddSelfHostConfiguration<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var translated = new Dictionary<string, string?>();

        Translate(builder.Configuration, translated, DatabaseConnectionStringKey, DatabaseUrlVariable, ToNpgsqlConnectionString);
        Translate(builder.Configuration, translated, CacheConnectionStringKey, RedisUrlVariable, ToRedisConfiguration);
        Translate(builder.Configuration, translated, AuthServiceAddressKey, AuthUrlVariable, NormaliseAuthServiceAddress);

        // Checked here even though this service only reads it as a yes/no, because this is the
        // one process that reads the value at all in the compose bundle. The Helm chart refuses
        // the same shapes while templating; compose has nowhere to do that, so a value that
        // could only ever fail arrives intact and its consequence lands on a stranger trying to
        // sign in. Startup is the last place left to say so.
        if (builder.Configuration[OriginVariable] is { Length: > 0 } origin)
        {
            translated[OriginVariable] = NormaliseConsoleOrigin(origin.Trim());
        }

        TranslateBrowserOrigins(builder.Configuration, translated);

        if (translated.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(translated);
        }

        return builder;
    }

    /// <summary>
    /// The origin a browser reaches the console on, when one has been configured. Absent under the
    /// AppHost, where Vite and Aspire's own hostnames decide it — which is why the caller treats
    /// absence as "not behind a proxy" rather than as a misconfiguration.
    /// </summary>
    public static string? GetConsoleOrigin(this IConfiguration configuration) =>
        configuration[OriginVariable] is { Length: > 0 } origin ? origin : null;

    /// <summary>
    /// Checks that the auth service's address is one, and removes a trailing slash — the JWKS
    /// address is built by concatenation, so a doubled slash would reach a different path.
    ///
    /// A path is deliberately allowed. Both consumers append to this value — YARP prepends the
    /// prefix to every forwarded path, and the JWKS address is assembled the same way — so an
    /// auth service mounted under one is reachable, and refusing it would rule out a deployment
    /// that works. A query or a fragment is not: there is nothing coherent to append to, and both
    /// consumers would silently build a URL nobody asked for.
    /// </summary>
    public static string NormaliseAuthServiceAddress(string value)
    {
        var address = value.TrimEnd('/');

        // `Uri` reads "auth:8080" as the scheme "auth", so parsing alone is not the test.
        if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(AuthUrlIsNotAnAddress);
        }

        if (url.Query.Length > 0 || url.Fragment.Length > 0)
        {
            throw new InvalidOperationException(AuthUrlCarriesMoreThanAnAddress);
        }

        return address;
    }

    /// <summary>
    /// Splits <c>FUTUREFLAGS_BROWSER_ORIGINS</c> into the indexed keys configuration binds an
    /// array from, checking each one is an origin.
    ///
    /// <para>
    /// Held to exactly the same shape as <see cref="OriginVariable"/>, by the same method, because
    /// it is compared against an <c>Origin</c> header — and a value carrying a path or a trailing
    /// slash can never match one. That failure would surface as a browser refusing a response for
    /// reasons it does not explain, which is among the least diagnosable things in web development.
    /// Better to refuse it at startup, where the message can name the variable.
    /// </para>
    /// </summary>
    private static void TranslateBrowserOrigins(
        IConfiguration configuration,
        Dictionary<string, string?> translated)
    {
        if (configuration[BrowserOriginsVariable] is not { Length: > 0 } configured)
        {
            return;
        }

        var origins = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormaliseConsoleOrigin)
            .ToList();

        for (var index = 0; index < origins.Count; index++)
        {
            translated[$"{Api.BrowserOrigins.ConfigurationKey}:{index}"] = origins[index];
        }
    }

    /// <summary>
    /// Checks that a configured origin is one, and removes a trailing slash.
    ///
    /// <para>
    /// What this cannot check is the thing most likely to be wrong — whether the value matches
    /// the URL in someone's address bar. It can check that no value would: a hostname with no
    /// scheme, or an origin carrying a path, never appears in an <c>Origin</c> header, so it
    /// cannot match whatever the browser sends. Those fail here rather than at a first sign-in
    /// that reports only <c>INVALID_ORIGIN</c>.
    /// </para>
    ///
    /// <para>
    /// A trailing slash is different in kind: it is a correct value written the way a person
    /// writes a URL, so it is normalised rather than rejected.
    /// </para>
    /// </summary>
    public static string NormaliseConsoleOrigin(string value)
    {
        var origin = value.TrimEnd('/');

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(OriginIsNotAnOrigin);
        }

        // Uri reports an absent path as "/", so that is the empty case rather than "".
        if (url.AbsolutePath is not "/" || url.Query.Length > 0 || url.Fragment.Length > 0)
        {
            throw new InvalidOperationException(OriginCarriesMoreThanAnOrigin);
        }

        return origin;
    }

    /// <summary>
    /// Whether to bring the database up to the latest migration during startup.
    ///
    /// On by default in Development, which is what the AppHost relies on. Off everywhere else
    /// unless asked for: the compose bundle sets it because it is single-replica by construction,
    /// while the Helm chart defaults to running migrations as a job instead.
    /// </summary>
    public static bool ShouldApplyMigrations(this IConfiguration configuration, IHostEnvironment environment) =>
        configuration.IsMigrateOnly()
        || (configuration.GetValue<bool?>(ApplyMigrationsVariable) ?? environment.IsDevelopment());

    /// <summary>
    /// Whether to migrate and then exit rather than go on to serve.
    ///
    /// What makes a migration something a deployment can order. The Helm chart's pre-upgrade job
    /// runs the server this way so that a failed migration fails the release before any deployment
    /// is touched — a server that migrated and then started serving would never finish the job.
    /// </summary>
    public static bool IsMigrateOnly(this IConfiguration configuration) =>
        configuration.GetValue<bool>(MigrateOnlyVariable);

    private static void Translate(
        IConfigurationManager configuration,
        Dictionary<string, string?> translated,
        string key,
        string variable,
        Func<string, string> translation)
    {
        // An already-populated key means Aspire (or the operator, directly) got there first.
        if (!string.IsNullOrWhiteSpace(configuration[key]))
        {
            return;
        }

        if (configuration[variable] is not { Length: > 0 } value)
        {
            return;
        }

        translated[key] = translation(value.Trim());
    }

    /// <summary>
    /// Turns a <c>postgres://</c> URL into the keyword-value string Npgsql expects.
    ///
    /// Only such a URL is accepted, because this variable configures two services and the other
    /// one is Node: <c>auth/src/config.ts</c> hands it to <c>new URL()</c>. A native Npgsql
    /// connection string would start this server and crashloop the auth container on a
    /// <c>TypeError</c> naming neither the variable nor the reason — the operator having been
    /// told the value was fine. One shared variable can only have one accepted format.
    /// </summary>
    public static string ToNpgsqlConnectionString(string value)
    {
        var looksLikeUrl =
            value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (!TryParseUrl(value, out var url, "postgres", "postgresql"))
        {
            // Two different mistakes, so two different messages: a postgres:// URL that will not
            // parse is almost always an unescaped character in the password, while anything else
            // is someone writing a format this variable does not take.
            throw new InvalidOperationException(looksLikeUrl ? UnparseableDatabaseUrl : DatabaseUrlIsNotAUrl);
        }

        // A '/' in the password ends the authority early, and everything after it — including the
        // '@' — lands in the path. The result still parses, into a connection with the wrong host,
        // the wrong database, and no credentials whatsoever, which then fails somewhere with no
        // trace of the cause. An '@' that did not become the userinfo delimiter is the tell.
        if (url.UserInfo.Length == 0 && value.Contains('@'))
        {
            throw new InvalidOperationException(UnparseableDatabaseUrl);
        }

        // Npgsql treats an absent database as "the one named after the user", so an empty path
        // here does not fail — it connects somewhere else. Either that database is missing, and
        // the error names neither this variable nor the reason, or it exists and is somebody
        // else's. Both services share this URL, so both would wander off to the same wrong place.
        var database = Unescape(url.AbsolutePath.TrimStart('/'));

        if (database.Length == 0)
        {
            throw new InvalidOperationException(DatabaseUrlNamesNoDatabase);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = url.Host,
            Port = url.Port > 0 ? url.Port : DefaultPostgresPort,
            Database = database
        };

        var (user, password) = SplitUserInfo(url.UserInfo);

        if (user is not null)
        {
            builder.Username = user;
        }

        if (password is not null)
        {
            builder.Password = password;
        }

        // Hosted Postgres providers put meaningful settings here — `?sslmode=require` most of all.
        // Dropping an unrecognised one silently could quietly downgrade a connection's security,
        // so an unusable parameter is reported rather than ignored.
        foreach (var (parameter, parameterValue) in ParseQuery(url.Query))
        {
            try
            {
                builder[parameter] = parameterValue;
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"{DatabaseUrlVariable} carries the query parameter '{parameter}', which Npgsql does not recognise. " +
                    "Remove it, or set the variable to a native Npgsql connection string instead.",
                    exception);
            }
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Turns a <c>redis://</c> URL into StackExchange.Redis' comma-separated configuration string.
    /// As with Postgres, anything else is passed through.
    ///
    /// A password containing a comma cannot survive this translation, because that is the
    /// separator StackExchange.Redis uses and it offers no escape for it. That case is rejected
    /// rather than mangled — see below.
    /// </summary>
    public static string ToRedisConfiguration(string value)
    {
        if (!TryParseUrl(value, out var url, "redis", "rediss"))
        {
            return value;
        }

        var port = url.Port > 0 ? url.Port : DefaultRedisPort;
        var options = new List<string> { $"{url.Host}:{port}" };

        var (user, password) = SplitUserInfo(url.UserInfo);

        if (password is not null)
        {
            // Emitting this anyway would split the password at the comma and hand the first
            // fragment to Redis, which fails to authenticate for a reason nothing on either side
            // reports. Refusing outright is the only honest option, and it names the way out.
            if (password.Contains(','))
            {
                throw new InvalidOperationException(
                    $"{RedisUrlVariable} carries a password containing a comma, which is the separator " +
                    "StackExchange.Redis uses to split its configuration and cannot be escaped. Set the " +
                    "variable to a native StackExchange.Redis configuration string instead, or use a " +
                    "password without a comma.");
            }

            options.Add($"password={password}");
        }

        // Redis 6 ACL users. The URL's `default` user is what a passwordless server assumes
        // anyway, so carrying it across would only add noise.
        if (user is { Length: > 0 } and not "default")
        {
            options.Add($"user={user}");
        }

        if (url.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase))
        {
            options.Add("ssl=True");
        }

        if (url.AbsolutePath.TrimStart('/') is { Length: > 0 } database)
        {
            options.Add($"defaultDatabase={database}");
        }

        return string.Join(',', options);
    }

    private static bool TryParseUrl(string value, [NotNullWhen(true)] out Uri? url, params string[] schemes)
    {
        url = null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!schemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        url = parsed;
        return true;
    }

    private static (string? User, string? Password) SplitUserInfo(string userInfo)
    {
        if (userInfo.Length == 0)
        {
            return (null, null);
        }

        var separator = userInfo.IndexOf(':');

        return separator < 0
            ? (Unescape(userInfo), null)
            : (Unescape(userInfo[..separator]), Unescape(userInfo[(separator + 1)..]));
    }

    private static IEnumerable<(string Name, string Value)> ParseQuery(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');

            // Skipping a segment that is not name=value would put back exactly the behaviour the
            // rest of this method exists to avoid: `?sslmode` is a plausible way to mistype
            // `?sslmode=require`, and dropping it leaves the connection unencrypted with nothing
            // said about it.
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    $"{DatabaseUrlVariable} carries the query segment '{pair}', which is not in name=value form. " +
                    "Write it as name=value, remove it, or set the variable to a native Npgsql connection " +
                    "string instead.");
            }

            yield return (Unescape(pair[..separator]), Unescape(pair[(separator + 1)..]));
        }
    }

    /// <summary>
    /// Credentials reach these URLs percent-encoded, because a password containing <c>@</c> or
    /// <c>/</c> could not be written in one otherwise.
    /// </summary>
    private static string Unescape(string value) => Uri.UnescapeDataString(value);
}
