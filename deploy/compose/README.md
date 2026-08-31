# FutureFlags with Docker Compose

For a single host — a VPS, or a machine under a desk. If you run Kubernetes, use the chart in
`../helm` instead.

```sh
cp .env.example .env
$EDITOR .env          # three values: origin, auth secret, database password
docker compose up -d
```

Then open the origin you set and create an account. **The first account to sign up becomes the
admin**; everyone after it is an ordinary user, and there is no seeded credential to change.

## What is running

| Service    | Published | Purpose |
|------------|-----------|---------|
| `caddy`    | 80, 443   | TLS termination. The only thing bound to a host port. |
| `server`   | no        | The API, and the console's static files. |
| `auth`     | no        | Better Auth. Reached only through the server's `/api/auth` forwarder. |
| `postgres` | no        | Flags, and the auth schema. Persisted in the `postgres-data` volume. |
| `redis`    | no        | Cache. Holds nothing worth keeping. |

**Do not publish a port for `auth`.** The browser is meant to reach it only through the server,
on the console's own origin, because that is what keeps the session cookie first-party. A second
origin does not add a capability — it breaks sign-in.

## Upgrading

```sh
docker compose pull
docker compose up -d
```

The server applies pending migrations as it starts (`FUTUREFLAGS_APPLY_MIGRATIONS=true`), which
is sound here because this file runs one replica of each service. Read the release notes first,
and take a backup — a migration is not reversible by restarting the old image.

If you scale `server` past one replica, turn that variable off and migrate deliberately instead.

## Using your own database

Set `FUTUREFLAGS_DATABASE_URL` in `.env` and comment the `postgres` service out of
`docker-compose.yml`, or it will keep running unused. Both `server` and `auth` read that one
variable — they share a database and separate by schema, so it must be the same one for each.

`FUTUREFLAGS_REDIS_URL` works the same way.

## When sign-in does not work

Nearly always `FUTUREFLAGS_ORIGIN` not matching the URL actually in the browser's address bar —
`https://` versus `http://`, or a `www.` that is really there. The auth service rejects requests
from an origin it does not trust, and the failure surfaces at sign-in rather than at startup.

Startup catches only the shapes that could never match anything: a missing scheme, or a path on
the end. A wrong hostname is a perfectly well-formed origin, so it gets this far.

Check what the service was given, and that it matches exactly:

```sh
docker compose exec auth printenv BETTER_AUTH_TRUSTED_ORIGINS
docker compose logs auth
```

`docker compose ps` is the other thing worth looking at: if `auth` is unhealthy, it could not
reach the database or has not migrated, and `server` will be waiting on it rather than starting.
