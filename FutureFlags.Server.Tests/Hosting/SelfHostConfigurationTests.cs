using FutureFlags.Server.Api;
using FutureFlags.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace FutureFlags.Server.Tests.Hosting;

/// <summary>
/// These variables are the contract with whoever deploys this, so the translation is worth
/// pinning down: getting it wrong means a connection string that silently drops a password or
/// an SSL mode, which fails somewhere far away from the cause.
/// </summary>
public class SelfHostConfigurationTests
{
    [Fact]
    public void ToNpgsqlConnectionString_TranslatesAPostgresUrl()
    {
        var result = SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgres://flags:s3cret@db.example.com:6432/futureflagsdb");

        var settings = Parse(result);

        Assert.Equal("db.example.com", settings["Host"]);
        Assert.Equal("6432", settings["Port"]);
        Assert.Equal("flags", settings["Username"]);
        Assert.Equal("s3cret", settings["Password"]);
        Assert.Equal("futureflagsdb", settings["Database"]);
    }

    [Fact]
    public void ToNpgsqlConnectionString_DefaultsThePort()
    {
        var settings = Parse(SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgresql://flags:s3cret@db.example.com/futureflagsdb"));

        Assert.Equal("5432", settings["Port"]);
    }

    [Fact]
    public void ToNpgsqlConnectionString_DecodesEscapedCredentials()
    {
        // A password containing @ or / cannot be written in a URL any other way, so a provider
        // handing one out percent-encodes it. Passing it through encoded would fail to authenticate.
        var settings = Parse(SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgres://flags%40prod:p%40ss%2Fword@db.example.com/futureflagsdb"));

        Assert.Equal("flags@prod", settings["Username"]);
        Assert.Equal("p@ss/word", settings["Password"]);
    }

    [Fact]
    public void ToNpgsqlConnectionString_CarriesQueryParametersAcross()
    {
        var settings = Parse(SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgres://flags:s3cret@db.example.com/futureflagsdb?sslmode=require"));

        Assert.Equal("Require", settings["SSL Mode"]);
    }

    [Fact]
    public void ToNpgsqlConnectionString_RejectsAnUnrecognisedQueryParameter()
    {
        // Silently dropping one could downgrade a connection that was meant to be encrypted.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.ToNpgsqlConnectionString(
                "postgres://flags:s3cret@db.example.com/futureflagsdb?schema=public"));

        Assert.Contains("schema", exception.Message);
        Assert.Contains(SelfHostConfiguration.DatabaseUrlVariable, exception.Message);
    }

    [Theory]
    // Unparseable: the '/' ends the authority and 'ab' is not a port.
    [InlineData("postgres://flags:ab/cd@db.example.com:5432/futureflagsdb")]
    // Worse — this one parses, into host 'flags', port 12, no credentials at all, and a database
    // named 'xyz@db.example.com:5432/futureflagsdb'. Left alone it fails far from the cause.
    [InlineData("postgres://flags:12/xyz@db.example.com:5432/futureflagsdb")]
    // The other accepted scheme reaches the same message rather than the wrong-format one, which
    // is what stops an operator whose provider hands out postgresql:// from suspecting the scheme.
    [InlineData("postgresql://flags:ab/cd@db.example.com:5432/futureflagsdb")]
    public void ToNpgsqlConnectionString_RejectsAPasswordThatBreaksTheUrl(string url)
    {
        // Generating a password with `openssl rand -base64` produces exactly this, because base64
        // contains '/'. The advice to use hex lives in the message.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.ToNpgsqlConnectionString(url));

        Assert.Contains(SelfHostConfiguration.DatabaseUrlVariable, exception.Message);
        Assert.Contains("percent-encoded", exception.Message);
    }

    [Fact]
    public void ToNpgsqlConnectionString_AcceptsAPercentEncodedPassword()
    {
        var settings = Parse(SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgres://flags:ab%2Fcd@db.example.com:5432/futureflagsdb"));

        Assert.Equal("ab/cd", settings["Password"]);
        Assert.Equal("db.example.com", settings["Host"]);
    }

    [Fact]
    public void ToNpgsqlConnectionString_ReadsPlusAsAPlusRatherThanASpace()
    {
        // Form encoding writes a space as '+'; a URL does not, and this is a URL. Pinned because
        // the Helm chart composes one of these from an operator-supplied password: emitting a
        // space as '+' would hand Postgres a password nobody set, and it fails to authenticate
        // with nothing anywhere naming the password as the cause. The chart writes %20.
        // Npgsql quotes a value containing a space, so this reads it back through the builder
        // rather than the naive splitter the other cases use.
        var settings = new NpgsqlConnectionStringBuilder(SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgres://flags:ab%20cd+ef@db.example.com:5432/futureflagsdb"));

        Assert.Equal("ab cd+ef", settings.Password);
    }

    [Theory]
    // A plausible way to mistype `?sslmode=require`. Dropped silently, it would leave the
    // connection unencrypted with nothing anywhere saying so.
    [InlineData("postgres://flags:s3cret@db.example.com/futureflagsdb?sslmode", "sslmode")]
    [InlineData("postgres://flags:s3cret@db.example.com/futureflagsdb?sslmode=require&pooling", "pooling")]
    [InlineData("postgres://flags:s3cret@db.example.com/futureflagsdb?=require", "=require")]
    public void ToNpgsqlConnectionString_RejectsAQuerySegmentThatIsNotNameValue(string url, string segment)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.ToNpgsqlConnectionString(url));

        Assert.Contains(SelfHostConfiguration.DatabaseUrlVariable, exception.Message);
        Assert.Contains(segment, exception.Message);
    }

    [Theory]
    [InlineData("Host=db.example.com;Username=flags;Password=s3cret;Database=futureflagsdb")]
    [InlineData("mysql://flags:s3cret@db.example.com/futureflagsdb")]
    public void ToNpgsqlConnectionString_RejectsAnythingThatIsNotAPostgresUrl(string value)
    {
        // The auth service reads this same variable and parses it with `new URL()`. Accepting a
        // format only this service understands would start the server and crashloop the auth
        // container, which is a worse outcome than refusing the value where it is set.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.ToNpgsqlConnectionString(value));

        Assert.Contains(SelfHostConfiguration.DatabaseUrlVariable, exception.Message);
        Assert.Contains("postgres://", exception.Message);
    }

    [Theory]
    [InlineData("postgres://flags:s3cret@db.example.com:5432/")]
    [InlineData("postgres://flags:s3cret@db.example.com:5432")]
    [InlineData("postgres://flags:s3cret@db.example.com/?sslmode=require")]
    public void ToNpgsqlConnectionString_RejectsAUrlThatNamesNoDatabase(string url)
    {
        // Not a parse failure — this one succeeds and produces `Database=`, which Npgsql reads as
        // absent and fills with the user's name. The connection then goes somewhere real and
        // wrong, or fails against a database nobody meant to name. Both services share this URL,
        // so both would wander to the same place.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.ToNpgsqlConnectionString(url));

        Assert.Contains(SelfHostConfiguration.DatabaseUrlVariable, exception.Message);
        Assert.Contains("futureflagsdb", exception.Message);
    }

    [Fact]
    public void ToNpgsqlConnectionString_KeepsNpgsqlSettingsReachableAsQueryParameters()
    {
        // What makes refusing a native connection string cost nothing: the URL form reaches the
        // same settings, so there is no configuration that only the rejected format could express.
        var settings = Parse(SelfHostConfiguration.ToNpgsqlConnectionString(
            "postgres://flags:s3cret@db.example.com/futureflagsdb?Timeout=30&Maximum%20Pool%20Size=50"));

        Assert.Equal("30", settings["Timeout"]);
        Assert.Equal("50", settings["Maximum Pool Size"]);
    }

    [Theory]
    [InlineData("redis://cache.example.com:6380", "cache.example.com:6380")]
    [InlineData("redis://cache.example.com", "cache.example.com:6379")]
    [InlineData("redis://:s3cret@cache.example.com", "cache.example.com:6379,password=s3cret")]
    [InlineData("rediss://cache.example.com", "cache.example.com:6379,ssl=True")]
    [InlineData("redis://cache.example.com/3", "cache.example.com:6379,defaultDatabase=3")]
    public void ToRedisConfiguration_TranslatesARedisUrl(string url, string expected)
    {
        Assert.Equal(expected, SelfHostConfiguration.ToRedisConfiguration(url));
    }

    [Fact]
    public void ToRedisConfiguration_RejectsAPasswordContainingItsSeparator()
    {
        // Emitting this would split the password at the comma and authenticate with the first
        // fragment, which fails for a reason neither side reports.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.ToRedisConfiguration("redis://:se,cret@cache.example.com"));

        Assert.Contains(SelfHostConfiguration.RedisUrlVariable, exception.Message);
        Assert.Contains("comma", exception.Message);
    }

    [Fact]
    public void ToRedisConfiguration_PassesANativeConfigurationStringThrough()
    {
        const string native = "cache.example.com:6379,password=s3cret,abortConnect=false";

        Assert.Equal(native, SelfHostConfiguration.ToRedisConfiguration(native));
    }

    [Fact]
    public void AddSelfHostConfiguration_FillsTheKeysTheApplicationReads()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            [SelfHostConfiguration.DatabaseUrlVariable] = "postgres://flags:s3cret@db.example.com/futureflagsdb",
            [SelfHostConfiguration.RedisUrlVariable] = "redis://cache.example.com",
            [SelfHostConfiguration.AuthUrlVariable] = "http://auth:8080/"
        });

        Assert.Contains("db.example.com", configuration.GetConnectionString("futureflagsdb"));
        Assert.Equal("cache.example.com:6379", configuration.GetConnectionString("cache"));

        // Trailing slash removed: the JWKS address is built by concatenation.
        Assert.Equal("http://auth:8080", configuration["services:auth:http:0"]);
    }

    [Fact]
    public void AddSelfHostConfiguration_LeavesAspiresValuesAlone()
    {
        // The whole design depends on this: the AppHost has already injected these by the time
        // the translation runs, and a self-hosted variable must not shadow them.
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:futureflagsdb"] = "Host=aspire-postgres;Database=futureflagsdb",
            ["services:auth:http:0"] = "http://localhost:41234",
            [SelfHostConfiguration.DatabaseUrlVariable] = "postgres://flags:s3cret@db.example.com/futureflagsdb",
            [SelfHostConfiguration.AuthUrlVariable] = "http://auth:8080"
        });

        Assert.Equal("Host=aspire-postgres;Database=futureflagsdb", configuration.GetConnectionString("futureflagsdb"));
        Assert.Equal("http://localhost:41234", configuration["services:auth:http:0"]);
    }

    [Theory]
    [InlineData("http://auth:8080", "http://auth:8080")]
    // The JWKS address is built by concatenation, so a trailing slash would double.
    [InlineData("http://auth:8080/", "http://auth:8080")]
    // Allowed on purpose: the forwarder prepends this to every path it forwards and the JWKS
    // address is assembled the same way, so an auth service mounted under a prefix is reachable.
    // Refusing it would rule out a deployment that works — unlike an origin, where it cannot.
    [InlineData("https://internal.example.com/auth", "https://internal.example.com/auth")]
    public void NormaliseAuthServiceAddress_AcceptsAnAddressAndDropsATrailingSlash(string value, string expected)
    {
        Assert.Equal(expected, SelfHostConfiguration.NormaliseAuthServiceAddress(value));
    }

    [Theory]
    // Parses, into the scheme "auth" — which is why parsing alone is not the test.
    [InlineData("auth:8080")]
    [InlineData("//auth:8080")]
    [InlineData("ftp://auth:8080")]
    // A base address that things are appended to cannot carry either of these.
    [InlineData("http://auth:8080?tenant=acme")]
    [InlineData("http://auth:8080#fragment")]
    public void NormaliseAuthServiceAddress_RejectsWhatCannotBeDialled(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.NormaliseAuthServiceAddress(value));

        Assert.Contains(SelfHostConfiguration.AuthUrlVariable, exception.Message);
    }

    [Theory]
    [InlineData("https://flags.example.com", "https://flags.example.com")]
    [InlineData("https://flags.example.com:8443", "https://flags.example.com:8443")]
    // Written the way a person writes a URL. A correct value, so it is normalised rather than
    // refused — a browser's Origin header never carries the slash.
    [InlineData("https://flags.example.com/", "https://flags.example.com")]
    [InlineData("http://localhost:18080/", "http://localhost:18080")]
    public void NormaliseConsoleOrigin_AcceptsAnOriginAndDropsATrailingSlash(string value, string expected)
    {
        Assert.Equal(expected, SelfHostConfiguration.NormaliseConsoleOrigin(value));
    }

    [Theory]
    // A hostname is not an origin, and neither is a host:port — Uri reads the latter as a scheme.
    [InlineData("flags.example.com")]
    [InlineData("localhost:18080")]
    // The console's URL rather than its origin. Renders fine everywhere and matches nothing.
    [InlineData("https://flags.example.com/console")]
    [InlineData("https://flags.example.com?tenant=acme")]
    [InlineData("https://flags.example.com#top")]
    public void NormaliseConsoleOrigin_RejectsWhatABrowserWouldNeverSend(string value)
    {
        // Not a matter of taste: no browser puts any of these in an Origin header, so the auth
        // service could never match one. Refusing at startup beats INVALID_ORIGIN at a stranger's
        // first sign-in, which is the only other place it would be noticed.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelfHostConfiguration.NormaliseConsoleOrigin(value));

        Assert.Contains(SelfHostConfiguration.OriginVariable, exception.Message);
    }

    [Fact]
    public void AddSelfHostConfiguration_ChecksTheOriginAndNormalisesIt()
    {
        // The compose bundle has nowhere else to check it: the chart refuses these while
        // templating, and startup is the equivalent moment for a value read from a .env file.
        Assert.Throws<InvalidOperationException>(() => Build(new Dictionary<string, string?>
        {
            [SelfHostConfiguration.OriginVariable] = "https://flags.example.com/console"
        }));

        var configuration = Build(new Dictionary<string, string?>
        {
            [SelfHostConfiguration.OriginVariable] = "https://flags.example.com/"
        });

        Assert.Equal("https://flags.example.com", configuration.GetConsoleOrigin());
    }

    [Fact]
    public void AddSelfHostConfiguration_AddsNothingWhenNoVariablesAreSet()
    {
        var configuration = Build([]);

        Assert.Null(configuration.GetConnectionString("futureflagsdb"));
        Assert.Null(configuration["services:auth:http:0"]);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Production", "true", true)]
    [InlineData("Development", "false", false)]
    // Case-insensitive, which the auth service's own parsing has to match: one variable
    // configures both, and a "True" that only half of them honoured would migrate one schema
    // and not the other — the half that skipped being the one the other depends on.
    [InlineData("Production", "True", true)]
    [InlineData("Production", "TRUE", true)]
    [InlineData("Development", "False", false)]
    public void ShouldApplyMigrations_DefaultsToDevelopmentAndIsOverridable(
        string environmentName,
        string? variable,
        bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SelfHostConfiguration.ApplyMigrationsVariable] = variable
            })
            .Build();

        var environment = new StubEnvironment { EnvironmentName = environmentName };

        Assert.Equal(expected, configuration.ShouldApplyMigrations(environment));
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = nameof(SelfHostConfigurationTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("on")]
    public void ShouldApplyMigrations_RefusesAValueThatIsNotABoolean(string variable)
    {
        // Pinned because the auth service has to match it, not because .NET's binder is in doubt.
        // One variable configures both, and the auth service reading "yes" as false while this one
        // throws would skip the schema the server's own migration then puts a trigger on.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SelfHostConfiguration.ApplyMigrationsVariable] = variable
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            configuration.ShouldApplyMigrations(new StubEnvironment { EnvironmentName = Environments.Production }));
    }

    [Fact]
    public void ShouldApplyMigrations_IsImpliedByMigrateOnly()
    {
        // The chart's migration job sets only FUTUREFLAGS_MIGRATE_ONLY. Asking it to migrate and
        // then exit without that implying "migrate" would produce a job that does nothing at all
        // and reports success — the worst available outcome.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SelfHostConfiguration.MigrateOnlyVariable] = "true"
            })
            .Build();

        Assert.True(configuration.ShouldApplyMigrations(new StubEnvironment { EnvironmentName = Environments.Production }));
        Assert.True(configuration.IsMigrateOnly());
    }

    [Fact]
    public void IsMigrateOnly_IsOffWhenUnset()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.False(configuration.IsMigrateOnly());
    }

    [Fact]
    public void BrowserOrigins_AreSplitIntoTheArrayTheCorsPolicyBindsFrom()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            [SelfHostConfiguration.BrowserOriginsVariable] =
                "https://app.example.com, https://admin.example.com"
        });

        Assert.Equal(
            ["https://app.example.com", "https://admin.example.com"],
            configuration.GetBrowserOrigins());
    }

    [Fact]
    public void BrowserOrigins_AreEmptyWhenUnset() =>
        // The right default. An installation whose flags are only read by server-side code should
        // not be answering a cross-origin request at all.
        Assert.Empty(Build([]).GetBrowserOrigins());

    [Fact]
    public void BrowserOrigins_DropATrailingSlashRatherThanRefusingIt() =>
        // The same leniency FUTUREFLAGS_ORIGIN gets, from the same method. A trailing slash is
        // what a person types, and it has one unambiguous reading — unlike a path, which does not.
        Assert.Equal(
            ["https://app.example.com"],
            Build(new Dictionary<string, string?>
            {
                [SelfHostConfiguration.BrowserOriginsVariable] = "https://app.example.com/"
            }).GetBrowserOrigins());

    [Theory]
    [InlineData("https://app.example.com/app")]
    [InlineData("app.example.com")]
    [InlineData("https://app.example.com?a=1")]
    public void BrowserOrigins_RejectWhatCouldNeverMatchAnOriginHeader(string value)
    {
        // Held to the same shape as FUTUREFLAGS_ORIGIN, and for a sharper reason: a mismatch here
        // surfaces as a browser refusing a response without explaining itself, which is among the
        // least diagnosable failures there is. Startup can at least name the variable.
        var exception = Assert.Throws<InvalidOperationException>(() => Build(new Dictionary<string, string?>
        {
            [SelfHostConfiguration.BrowserOriginsVariable] = value
        }));

        Assert.Contains("origin", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserOrigins_IgnoreEmptyEntriesFromATrailingComma() =>
        Assert.Equal(
            ["https://app.example.com"],
            Build(new Dictionary<string, string?>
            {
                [SelfHostConfiguration.BrowserOriginsVariable] = "https://app.example.com,"
            }).GetBrowserOrigins());

    private static IConfiguration Build(Dictionary<string, string?> values)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production
        });

        builder.Configuration.AddInMemoryCollection(values);
        builder.AddSelfHostConfiguration();

        return builder.Configuration;
    }

    private static Dictionary<string, string> Parse(string connectionString) =>
        connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());
}
