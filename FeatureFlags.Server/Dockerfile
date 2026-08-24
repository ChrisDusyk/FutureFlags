# The server: the API, and the console's static files served from wwwroot alongside it.
#
# One image rather than two because the console is a single-page app that talks to this server
# and nothing else — same origin is what keeps the session cookie first-party, so splitting them
# would mean reintroducing CORS and a second hostname to configure. This stage arrangement is the
# container equivalent of the AppHost's `server.PublishWithContainerFiles(webfrontend, "wwwroot")`.
#
# Build from the repository root, which is the context:
#
#   docker build -f FeatureFlags.Server/Dockerfile -t featureflags-server .

FROM node:22-alpine AS frontend
WORKDIR /src

RUN corepack enable

COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY frontend/ ./
RUN pnpm build


FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH

# MinVer reads the version from a git tag, and the git directory is deliberately not in this
# context. The release workflow passes the tag it is building; anything else gets the default,
# which is honest about being an untagged build.
ARG VERSION=0.0.0-dev

WORKDIR /src

COPY global.json nuget.config ./

# Project files first, so that restore is cached against dependency changes rather than against
# every edit to a source file.
COPY FeatureFlags.Domain/FeatureFlags.Domain.csproj FeatureFlags.Domain/
COPY FeatureFlags.Infrastructure/FeatureFlags.Infrastructure.csproj FeatureFlags.Infrastructure/
COPY FeatureFlags.Server/FeatureFlags.Server.csproj FeatureFlags.Server/
# Both, and both before restore. The csproj files above name their packages without versions —
# Directory.Packages.props is where the versions are, so restore fails outright without it.
COPY Directory.Build.props Directory.Packages.props ./
RUN dotnet restore FeatureFlags.Server/FeatureFlags.Server.csproj -a "$TARGETARCH"

COPY FeatureFlags.Domain/ FeatureFlags.Domain/
COPY FeatureFlags.Infrastructure/ FeatureFlags.Infrastructure/
COPY FeatureFlags.Server/ FeatureFlags.Server/
# The evaluation engine is shared source that FeatureFlags.Domain compiles by <Compile Include>,
# not a project of its own — so nothing above brings it in. Restore does not need it and publish
# does, which is the same trap Directory.Packages.props sets a few lines up: the solution build
# stays green while the image build fails.
COPY shared/ shared/

RUN dotnet publish FeatureFlags.Server/FeatureFlags.Server.csproj \
    -a "$TARGETARCH" \
    -c Release \
    --no-restore \
    -p:MinVerVersionOverride="$VERSION" \
    -o /app


# Chiseled: no shell, no package manager, and a non-root user by default. It also means no curl
# or wget, so there is no HEALTHCHECK here — Kubernetes probes over HTTP without needing anything
# in the image, and in compose the ordering that matters is expressed on the auth container,
# whose readiness check is the one with a real precondition behind it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/ ./
COPY --from=frontend /src/dist/ ./wwwroot/

USER $APP_UID

ENTRYPOINT ["dotnet", "FeatureFlags.Server.dll"]
