# The auth service: Better Auth is a Node library, so identity runs as its own process rather
# than inside the .NET server. Build from the repository root or from auth/ — the context is
# this directory either way.
#
#   docker build -t futureflags-auth auth/

FROM node:22-alpine AS build
WORKDIR /src

RUN corepack enable

# Copied before the sources so that a change to the application does not invalidate the
# dependency layer. --frozen-lockfile is what makes the build reproducible rather than
# merely repeatable.
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY tsconfig.json ./
COPY src/ ./src/
RUN pnpm build

# Resolved again with the dev dependencies pruned. TypeScript and @types/* are build-time
# only, and carrying them into the runtime image would roughly double it.
RUN pnpm prune --prod


FROM node:22-alpine AS final
WORKDIR /app

ENV NODE_ENV=production

# node:22-alpine already defines an unprivileged `node` user. Nothing here needs to write to
# the filesystem, so it owns nothing it runs.
COPY --from=build --chown=node:node /src/node_modules/ ./node_modules/
COPY --from=build --chown=node:node /src/dist/ ./dist/
COPY --from=build --chown=node:node /src/package.json ./package.json

USER node

EXPOSE 8080
ENV PORT=8080

# Readiness rather than liveness: this probes for auth."user", so it answers healthy only once
# the schema has actually been migrated. The compose bundle depends on that — the server's own
# migration puts a trigger on that table, so it must not start before this passes.
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=12 \
  CMD wget --spider -q http://127.0.0.1:8080/health || exit 1

CMD ["node", "dist/server.js"]
