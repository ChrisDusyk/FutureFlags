# Self-hosting FeatureFlags

FeatureFlags runs as two containers — the server (the API, and the console served from its
`wwwroot`) and the auth service — against Postgres and Redis.

Two supported ways to run it:

- **[Docker Compose](../deploy/compose/README.md)** on a single host. Includes Caddy, so TLS is
  automatic and there is one origin to configure.
- **[Helm](../deploy/helm/featureflags/README.md)** on Kubernetes.

Both pull the same images from `ghcr.io/chrisdusyk/`.

## Quickstart

```sh
curl -fsSL https://github.com/ChrisDusyk/FeatureFlags/releases/latest/download/featureflags-compose.tar.gz | tar xz
cd compose
cp .env.example .env
$EDITOR .env
docker compose up -d
```

Open the origin you configured. **The first account to sign up becomes the admin**, and everyone
after it is an ordinary user. There is no seeded credential and no way to promote somebody
through the console yet, so sign up first, before anyone else can.

## Configuration

Both services read the same names, so one value configures both where they share a concern.

| Variable | Used by | |
|---|---|---|
| `FEATUREFLAGS_ORIGIN` | both | **Required.** The origin a browser loads the console on, scheme and port included. |
| `FEATUREFLAGS_DATABASE_URL` | both | `postgres://user:password@host:5432/featureflagsdb`. Npgsql's own settings work as query parameters. |
| `BETTER_AUTH_SECRET` | auth | **Required.** Signs sessions and tokens. `openssl rand -base64 32`. |
| `FEATUREFLAGS_REDIS_URL` | server | `redis://host:6379`. |
| `FEATUREFLAGS_AUTH_URL` | server | The auth service's in-network address, e.g. `http://auth:8080`. |
| `FEATUREFLAGS_APPLY_MIGRATIONS` | both | Migrate during startup. Safe at one replica. |
| `FEATUREFLAGS_MIGRATE_ONLY` | server | Migrate, then exit. For running migrations as a deliberate step. |

### Getting the origin right

This is the setting that goes wrong. `FEATUREFLAGS_ORIGIN` has to match what the browser puts in
its address bar **exactly** — scheme, hostname, and port:

- `https://flags.example.com` and `http://flags.example.com` are different origins.
- `https://flags.example.com` and `https://www.flags.example.com` are different origins.
- `http://localhost` and `http://localhost:8080` are different origins.

Startup checks the shape and nothing more: a value with no scheme, or one carrying a path, a
query, or a fragment, is refused where it is set, because no browser sends such a thing in an
`Origin` header and so no such value could ever match. The Helm chart refuses the same shapes
while templating, before anything is installed.

What that cannot check is the mistake above — whether the origin matches the address bar. Those
values are all well-formed, the auth service simply refuses requests from an origin it does not
trust, and the failure surfaces at the first sign-in attempt with an error that does not name the
cause. If sign-in returns `INVALID_ORIGIN`, this is why.

### Passwords that go into a URL

`FEATUREFLAGS_DATABASE_URL` is a URL, so a password inside it has to be URL-safe. Generate one
with `openssl rand -hex 24` rather than `-base64`: base64 output contains `/`, which ends the
authority portion of a URL. Depending on what follows it, such a URL either fails to parse or —
worse — parses into a connection with the wrong host, the wrong database, and no credentials at
all.

The server refuses both rather than acting on them, so this fails at startup with a message
naming the cause instead of somewhere far away. If a password containing `/`, `@`, `:`, or `#`
is unavoidable, percent-encode it: `p@ss/word` written as `p%40ss%2Fword` arrives intact.

That it has to be a URL at all is not a house style. The auth service is Node and parses this
same variable with `new URL()`, so a format only the .NET server understands would start one
half of the stack and crashloop the other — and the server refuses those too, for that reason.
Anything Npgsql accepts is reachable as a query parameter, so nothing is out of reach.

## Architecture, and one rule

```
browser ──▶ Caddy / ingress ──▶ server ──▶ /api/auth/*   ──▶ auth service ──▶ auth schema
                                       └─▶ /api/*        ──▶ (JWT bearer)  ──▶ public schema

your app ─▶ Caddy / ingress ──▶ server ──▶ /api/evaluation*  ──▶ (SDK key) ──▶ public schema
```

\* `GET /api/evaluation`, `GET /api/evaluation/ruleset`, and `POST /api/evaluation` — see
"Connecting" below for which one your key actually uses.

**Never expose the auth service directly.** Every deployment artifact here keeps it off the
public network on purpose. The browser reaches it only through the server's `/api/auth`
forwarder, which is what keeps the console on one origin and its session cookie first-party.
Publishing a port or adding an ingress rule for it does not add a capability — it creates a
second origin, and sign-in breaks.

The two services share one database and separate by schema: Better Auth owns `auth`, the
application owns `public`. There is no foreign key between them. `public.users` is a mirror
maintained by a trigger, not a source — nothing in this application authors an identity.

## Connecting an application

Your applications read flags with an **SDK key**: a credential a program holds, scoped to one
environment, that can read and nothing else. Issue one in the console under **Organization →
Environments**. You have to be an admin, which the first account to sign up is.

The token is shown once, when it is issued. Only a hash of it is stored, so it cannot be read back
out — if you lose it, revoke that key and issue another.

### Two kinds of key

The console asks where the key will run, and the answer decides which kind you get:

| | | |
|---|---|---|
| `ffs_…` | **secret** | a backend, a container, a CI job — anywhere only you can read it |
| `ffp_…` | **publishable** | a web or mobile app, where the key is shipped to the user |

Both read exactly the same thing. What differs is where each may be used from, and the server
enforces it: a request carrying an `Origin` header came from a browser, and a secret key presented
from one is refused. That is checked here rather than left to CORS, because a key published in a
JavaScript bundle can be copied out of it and replayed from anywhere.

**A publishable key is public.** Anyone who loads your app can read it, and with it every flag key
in that environment and whether each one is on. That is the trade — flag names travel further than
people expect, so name them accordingly.

### Reading flags from a browser

A browser also needs the server to allow its origin, which is `FEATUREFLAGS_BROWSER_ORIGINS`:

```sh
FEATUREFLAGS_BROWSER_ORIGINS=https://app.example.com,https://admin.example.com
```

Empty by default, and leave it so unless you have such an app — an installation read only by
server-side code should not be answering a cross-origin request at all. Each entry is a whole
origin, scheme included, because that is what a browser sends; a value carrying a path is refused
at startup, and by the Helm chart while templating.

This is what `POST /api/evaluation` checks as well — the route a publishable key reads flags
through when a flag has been narrowed to a segment. Nothing else to set: it is the same setting,
just backing a second route now.

The two settings are separate on purpose. The origin list decides who may *read* the answer; the
key kind decides what may be *presented*. Neither substitutes for the other.

### Connecting

An application needs two settings: the same origin the console is on, and the key.

```sh
FEATUREFLAGS_URL=https://flags.example.com
FEATUREFLAGS_SDK_KEY=ffs_prod_9f2a71c0d4e83b16_…
```

There is no environment to configure. The key carries it, which is why a staging key cannot read
production no matter what it is pointed at.

The endpoint underneath is `GET /api/evaluation`, which answers with the flag states for the key's
environment and an `ETag`. Send it back as `If-None-Match` and an unchanged poll costs a 304 with no
body — worth doing, because the answer is cached server-side for a few seconds and your poll
interval is what decides how quickly a toggle reaches you.

```sh
curl -sS -H "Authorization: Bearer $FEATUREFLAGS_SDK_KEY" \
  https://flags.example.com/api/evaluation
```

This answers for nobody in particular: a flag narrowed to a segment reads `false` here, because
nothing has said who is asking. Reading a flag for a particular person — one described by the
traits a segment is written against — uses one of two other routes, and which one depends on the
key kind, not on where the request comes from:

- A **secret** key can `GET /api/evaluation/ruleset` — the flag states *and* the segment
  definitions, meant to be fetched once and evaluated locally rather than per request.
- A **publishable** key `POST`s a context and gets booleans back, because segment definitions are
  not something a key expected to be public can be handed.

The client libraries (see `clients/README.md`) make this choice for you and evaluate on your
behalf; reach for these two routes directly only if you are not using one of them.

Revoking takes effect on the next request. Keys are never deleted, so a key that stopped working
stays distinguishable from one that never existed, and the console shows when each was last used —
which is what makes revoking an unfamiliar key a decision rather than a gamble.

## Migrations

Two schemas migrate independently, and the order is not optional: the server's migration puts a
trigger on `auth."user"`, so Better Auth has to create that table first.

The compose bundle handles this with its `depends_on` chain — the auth service reports healthy
only once that table exists, and the server waits for healthy. On Kubernetes the default
`migrations.mode: job` makes the order structural instead, with the auth migration as an init
container.

Migrating during startup is safe at exactly one replica of the server. It takes a Postgres
advisory lock, so two instances starting together serialise rather than race — but before
running more than one deliberately, migrate as a step of its own:

```sh
# the auth schema first
docker compose run --rm auth node dist/migrate-cli.js
# then the application schema
docker compose run --rm -e FEATUREFLAGS_MIGRATE_ONLY=true server
```

## Upgrading

```sh
docker compose pull && docker compose up -d
```

Read the release notes, and take a backup first. A migration is not undone by starting the old
image again, and `helm rollback` returns manifests rather than schemas.

Pin `FEATUREFLAGS_VERSION` rather than tracking `latest` once you are past trying it out — an
unattended `docker compose pull` against a moving tag is how an upgrade happens by accident.

## Backups

Everything worth keeping is in Postgres — both schemas. Nothing in Redis matters, and the
containers hold no state.

```sh
docker compose exec -T postgres pg_dump -U featureflags -Fc featureflagsdb > featureflags.dump
```

Restore into an empty database:

```sh
docker compose exec -T postgres pg_restore -U featureflags -d featureflagsdb --clean < featureflags.dump
```

Dump both schemas together. Restoring `public` against a different `auth` leaves the mirrored
`public.users` rows pointing at identities that no longer exist.

The bundled Postgres has no backups, no failover, and one replica. It is there so the first run
works. For anything whose loss would matter, point `FEATUREFLAGS_DATABASE_URL` at a database
somebody maintains.

## Health

| Path | |
|---|---|
| `/health` | Readiness. Covers the database and the cache. |
| `/alive` | Liveness. The process is answering. |

Both are unauthenticated and return a status word and nothing else — no check names, no
durations, no exception detail.

## Observability

Set `OTEL_EXPORTER_OTLP_ENDPOINT` and both services export traces, metrics, and logs. Unset,
nothing is exported and no collector is required.
