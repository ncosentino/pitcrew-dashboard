# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS frontend
WORKDIR /src
COPY src/PitCrew.Dashboard.WebApi/ClientApp/package*.json ./
RUN --mount=type=cache,target=/root/.npm \
    if [ -f package-lock.json ]; then \
        npm ci; \
    else \
        npm install --no-audit --no-fund; \
    fi
COPY src/PitCrew.Dashboard.WebApi/ClientApp/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS publish
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN target_arch="$TARGETARCH"; \
    if [ "$target_arch" = "amd64" ]; then target_arch="x64"; fi; \
    dotnet restore src/PitCrew.Dashboard.WebApi/PitCrew.Dashboard.WebApi.csproj \
        --runtime "linux-musl-$target_arch" \
        -p:SelfContained=false
RUN target_arch="$TARGETARCH"; \
    if [ "$target_arch" = "amd64" ]; then target_arch="x64"; fi; \
    dotnet publish src/PitCrew.Dashboard.WebApi/PitCrew.Dashboard.WebApi.csproj \
        --configuration Release \
        --runtime "linux-musl-$target_arch" \
        --self-contained false \
        --no-restore \
        -p:SkipSpaBuild=true \
        --output /out
RUN mkdir -p /dashboard-data
COPY --from=frontend /src/dist/ /out/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish --chown=$APP_UID:$APP_UID /out/ ./
COPY --from=publish --chown=$APP_UID:$APP_UID /dashboard-data/ /var/lib/pitcrew-dashboard/
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --spider http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["./PitCrew.Dashboard.WebApi"]
