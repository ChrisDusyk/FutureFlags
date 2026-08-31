# FutureFlags

[![server-ci](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/server-ci.yml/badge.svg)](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/server-ci.yml)
[![frontend-ci](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/frontend-ci.yml/badge.svg)](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/frontend-ci.yml)
[![auth-ci](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/auth-ci.yml/badge.svg)](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/auth-ci.yml)
[![clients-ci](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/clients-ci.yml/badge.svg)](https://github.com/ChrisDusyk/FutureFlags/actions/workflows/clients-ci.yml)
[![latest release](https://img.shields.io/github/v/release/ChrisDusyk/FutureFlags)](https://github.com/ChrisDusyk/FutureFlags/releases/latest)
[![NuGet](https://img.shields.io/nuget/v/FutureFlags.Client)](https://www.nuget.org/packages/FutureFlags.Client)
[![npm](https://img.shields.io/npm/v/%40futureflags%2Fclient)](https://www.npmjs.com/package/@futureflags/client)
[![license](https://img.shields.io/github/license/ChrisDusyk/FutureFlags)](LICENSE)

A self-hosted feature flag platform: toggle and target functionality at runtime without a
redeploy, from an admin console you run yourself.

- **Flags per environment.** A flag's identity is global; its on/off state and targeting are set
  independently per environment.
- **Segments.** Named groups — beta testers, internal staff, one account being debugged — that a
  flag can be narrowed to, defined once and reused across every rule that targets them.
- **Two SDK key kinds.** A secret key (`ffs_`) fetches the whole ruleset and evaluates in-process;
  a publishable key (`ffp_`) is safe to ship to a browser and never receives segment definitions.
- **Client libraries** for .NET and Node, evaluating against the same rules the server does — see
  [`clients/`](clients/).
- **First account in becomes admin.** No seeded credential, no separate provisioning step.

## How it's built

A .NET 10 API (`FutureFlags.Server`) orchestrated by [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/),
following Domain-Driven Design with vertical slice architecture, railway-oriented error handling,
and an `Option` type in place of nulls. Identity is owned by [Better Auth](https://www.better-auth.com/),
which runs as its own Node service (`auth/`) behind the same origin as the API. The admin console
(`frontend/`) is a React + Vite SPA.

```
FutureFlags.AppHost/            Aspire orchestration (Postgres, Redis, auth, server, frontend)
FutureFlags.Domain/             Entities, value objects, Shared/ (Result, Option) — zero project references
FutureFlags.Infrastructure/     EF Core AppDbContext, Postgres, repository implementations
FutureFlags.Server/             API host — Features/ holds vertical slices
auth/                            Node service hosting Better Auth (Hono)
frontend/                        React + Vite admin console
clients/                         .NET and Node SDKs published to NuGet and npm
shared/evaluation/               The flag-evaluation logic shared verbatim by server and .NET client,
                                  held to the same answers as the Node client by a conformance suite
deploy/                          Docker Compose bundle and Helm chart for self-hosting
```

For the full architectural rules — persistence, authentication, the evaluation routes, frontend
conventions, testing — see [`CLAUDE.md`](CLAUDE.md), which is both the contributor guide and the
canonical description of how the pieces fit together.

## Running it locally

Requires the [Aspire CLI](https://learn.microsoft.com/dotnet/aspire/cli/overview). From the
repository root:

```sh
aspire run
```

This starts Postgres, Redis, the auth service, the API, and the Vite dev server together, and
opens the Aspire dashboard for logs and traces. The first account you sign up through the console
becomes the admin.

## Self-hosting

FutureFlags ships as two container images — the server and the auth service — deployable via
Docker Compose or Helm. See **[docs/self-hosting.md](docs/self-hosting.md)** for the quickstart,
configuration reference, and upgrade notes.

## License

[MIT](LICENSE)
