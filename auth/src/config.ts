/**
 * Everything the auth service needs from its environment, read once at startup so a
 * misconfigured resource fails immediately with the name of the missing variable
 * rather than at somebody's first sign-in.
 *
 * The values come from Aspire: the AppHost's `WithReference(futureFlagsDb)` injects
 * the `FUTUREFLAGSDB_*` connection properties, and the rest are set explicitly there.
 */

/** The Postgres schema Better Auth owns. Nothing the application writes lives here. */
export const authSchema = 'auth';

/**
 * Fixed rather than derived from the base URL: the .NET API validates these two
 * claims, and it should not have to be reconfigured because a hostname changed
 * between development and production. Trust comes from the JWKS signature.
 */
export const jwtIssuer = 'futureflags-auth';
export const jwtAudience = 'futureflags-api';

export interface DatabaseSettings {
  host: string;
  port: number;
  user: string;
  password: string;
  database: string;
}

/**
 * A boolean the .NET server reads from the same variable, so it is read the same way.
 *
 * Case-insensitively, because .NET's configuration binding takes "True" as readily as "true",
 * and one variable configuring both services means a stricter reading here would migrate one
 * schema and not the other. Strictly otherwise, for the same reason inverted: .NET *throws* on
 * a value that is not a boolean, so treating "yes" or "1" as false would have this service
 * quietly skip the schema while the server refused to start at all — over one value, set once,
 * meant for both.
 */
function readBoolean(name: string, fallback: boolean): boolean {
  const value = process.env[name]?.trim();

  // Unset and empty are the same thing to .NET's binder: absent, so the default applies.
  if (!value) {
    return fallback;
  }

  const normalised = value.toLowerCase();

  if (normalised === 'true') {
    return true;
  }

  if (normalised === 'false') {
    return false;
  }

  throw new Error(
    `${name} has to be true or false — got "${value}". The .NET server reads this same variable ` +
      'and refuses anything else, so a value it rejects cannot be one this service acts on.',
  );
}

function required(name: string): string {
  const value = process.env[name];

  if (!value) {
    throw new Error(`${name} is not set. Run the app through the Aspire AppHost.`);
  }

  return value;
}

function readDatabaseSettings(): DatabaseSettings {
  // Aspire hands non-.NET resources both a URI and the discrete properties. The URI
  // is the one documented for JavaScript apps, so prefer it and fall back to the parts.
  //
  // FUTUREFLAGS_DATABASE_URL is the same thing under the name a self-hosting operator
  // sets, shared with the server so that one variable configures both. Aspire's own
  // variable is checked first, for the same reason the server's translation defers to
  // it: under the AppHost, Aspire is the authority.
  const uri = process.env.FUTUREFLAGSDB_URI ?? process.env.FUTUREFLAGS_DATABASE_URL;

  if (uri) {
    const name = process.env.FUTUREFLAGSDB_URI ? 'FUTUREFLAGSDB_URI' : 'FUTUREFLAGS_DATABASE_URL';

    // `new URL()` on something that is not one throws a TypeError naming neither the variable
    // nor what was wrong with it, and this runs at import: the container would exit on a stack
    // trace with no way back to the value that caused it. The server rejects the same input for
    // the same reason, so whichever half of the stack starts first says the same thing.
    let parsed: URL;

    try {
      parsed = new URL(uri);
    } catch {
      throw new Error(
        `${name} has to be a postgres:// or postgresql:// URL, e.g. postgres://user:password@host:5432/futureflagsdb. ` +
          'The .NET server reads this same variable, so both accept the one format. A password ' +
          "containing '/', '@', ':' or '#' has to be percent-encoded ('/' as %2F, '@' as %40).",
      );
    }

    // A URL of any scheme parses, and every field below reads the same on one — so `mysql://`
    // would be taken apart happily here and handed to a Postgres driver, which then fails
    // against a host that was never the problem. The server refuses the scheme outright, and
    // this variable configures both, so accepting it in one of them is how they drift.
    if (parsed.protocol !== 'postgres:' && parsed.protocol !== 'postgresql:') {
      throw new Error(
        `${name} has the scheme ${parsed.protocol.replace(/:$/, '')}://, but this is a Postgres ` +
          'connection: it has to be postgres:// or postgresql://. Both services read this one ' +
          'variable, and the .NET server refuses the same value.',
      );
    }

    // An empty path is not an error to the driver, which falls back to a database named after
    // the user — so this would connect somewhere, just not here. The server refuses the same
    // value for the same reason: one URL, one database, both services or neither.
    const database = parsed.pathname.replace(/^\//, '');

    if (!database) {
      throw new Error(
        `${name} names no database — there is nothing after the host. Write it as ` +
          'postgres://user:password@host:5432/futureflagsdb. Left out, the driver connects to a ' +
          "database named after the user instead, which is either missing or somebody else's.",
      );
    }

    return {
      host: parsed.hostname,
      port: parsed.port ? Number(parsed.port) : 5432,
      user: decodeURIComponent(parsed.username),
      password: decodeURIComponent(parsed.password),
      database,
    };
  }

  return {
    host: required('FUTUREFLAGSDB_HOST'),
    port: Number(required('FUTUREFLAGSDB_PORT')),
    user: required('FUTUREFLAGSDB_USERNAME'),
    password: required('FUTUREFLAGSDB_PASSWORD'),
    database: required('FUTUREFLAGSDB_DATABASENAME'),
  };
}

export const database = readDatabaseSettings();

export const port = Number(process.env.PORT ?? 3000);

export const secret = required('BETTER_AUTH_SECRET');

/**
 * The origin a browser sees is the console's, not this service's — every request
 * arrives through the server's `/api/auth` forwarder. The AppHost cannot hand that
 * endpoint over without making the resource graph circular (frontend → server → auth),
 * so in development it goes unset and the trusted origins below carry the real check.
 */
export const baseUrl = process.env.BETTER_AUTH_URL ?? `http://localhost:${port}`;

/**
 * The origins Better Auth will accept a request from, as exact values or wildcard
 * patterns such as `http://localhost:*`. In development this covers whichever port
 * Vite happened to take; in production it is the console's real origin.
 */
export const trustedOrigins = (process.env.BETTER_AUTH_TRUSTED_ORIGINS ?? '')
  .split(',')
  .map((origin) => origin.trim())
  .filter((origin) => origin.length > 0);

const isProduction = process.env.NODE_ENV === 'production';

/**
 * Whether to reconcile the `auth` schema during startup.
 *
 * Mirrors the server's `FUTUREFLAGS_APPLY_MIGRATIONS`, and defaults the same way: on
 * outside production, which is what the AppHost relies on. The compose bundle turns it
 * on explicitly because it runs one replica of each service; the Helm chart leaves it
 * off and runs `pnpm migrate` as a job instead.
 *
 * Ordering matters wherever this is decided: the server's own migration puts a trigger
 * on `auth."user"`, so this has to have run before that one does. What enforces it is
 * the readiness check in server.ts, which stays 503 until the table exists.
 */
export const applyMigrations = readBoolean('FUTUREFLAGS_APPLY_MIGRATIONS', !isProduction);
