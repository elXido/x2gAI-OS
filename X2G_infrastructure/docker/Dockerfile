# syntax=docker/dockerfile:1
# Build context is the repo root (see docker-compose.yml). Three stages:
# the spec's "Stage 1" (Angular SPA -> static assets) plus a Fable compile
# stage it implies but cannot name — the daemon JS has to come from dotnet,
# which must not exist in the runtime image — then the Node runtime.

# ---------------------------------------------------------------------------
# Stage 1 — Angular SPA -> static assets
# Requires the Angular workspace (package.json, angular.json) to exist under
# frontend/; until then build only the daemon target:
#   docker compose build x2g-daemon
# ---------------------------------------------------------------------------
FROM node:22-slim AS spa-build
WORKDIR /spa
COPY X2G_Ventures/x2gAI-OS/frontend/package*.json ./
RUN npm ci
COPY X2G_Ventures/x2gAI-OS/frontend/ ./
RUN npm run build

# ---------------------------------------------------------------------------
# Stage 2 — Fable compile: F# -> ES2015 JS
# SDK 10 matches the net10.0 target and is the minimum that can unpack the
# Fable 5.x dotnet-tool package format.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS daemon-build
WORKDIR /build
COPY dotnet-tools.json ./
RUN dotnet tool restore
COPY X2G_Ventures/x2gAI-OS/src/ ./src/
RUN dotnet fable src/App.fsproj --outDir /build/out

# ---------------------------------------------------------------------------
# Stage 3 — runtime: headless Node daemon serving SPA + WebSocket telemetry
# WORKDIR /x2g mirrors the repo root because the daemon resolves
# X2G_infrastructure/ and X2G_Ventures/ from process.cwd().
# ---------------------------------------------------------------------------
FROM node:22-slim AS runtime
ENV NODE_ENV=production
WORKDIR /x2g

COPY X2G_Ventures/x2gAI-OS/package.json X2G_Ventures/x2gAI-OS/package-lock.json ./X2G_Ventures/x2gAI-OS/
RUN cd X2G_Ventures/x2gAI-OS && npm ci --omit=dev

COPY X2G_infrastructure/config/ ./X2G_infrastructure/config/
COPY X2G_infrastructure/templates/ ./X2G_infrastructure/templates/
COPY --from=daemon-build /build/out/ ./X2G_Ventures/x2gAI-OS/build/
COPY --from=spa-build /spa/dist/ ./X2G_Ventures/x2gAI-OS/frontend/dist/

EXPOSE 4280 4287
CMD ["node", "X2G_Ventures/x2gAI-OS/build/Daemon.js"]

# ---------------------------------------------------------------------------
# Daemon-only target while the Angular workspace does not exist yet:
# skips spa-build; the daemon serves 503 for the SPA until dist/ is mounted.
# ---------------------------------------------------------------------------
FROM node:22-slim AS runtime-daemon-only
ENV NODE_ENV=production
WORKDIR /x2g

COPY X2G_Ventures/x2gAI-OS/package.json X2G_Ventures/x2gAI-OS/package-lock.json ./X2G_Ventures/x2gAI-OS/
RUN cd X2G_Ventures/x2gAI-OS && npm ci --omit=dev

COPY X2G_infrastructure/config/ ./X2G_infrastructure/config/
COPY X2G_infrastructure/templates/ ./X2G_infrastructure/templates/
COPY --from=daemon-build /build/out/ ./X2G_Ventures/x2gAI-OS/build/

EXPOSE 4280 4287
CMD ["node", "X2G_Ventures/x2gAI-OS/build/Daemon.js"]
