# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS publish
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet restore src/PitCrew.Connector.App/PitCrew.Connector.App.csproj \
        -a "$TARGETARCH" \
        -p:SelfContained=false
RUN dotnet publish src/PitCrew.Connector.App/PitCrew.Connector.App.csproj \
        --configuration Release \
        --arch "$TARGETARCH" \
        --self-contained false \
        --no-restore \
        --output /out
RUN mkdir -p /connector-data

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS final
WORKDIR /app
ENV DOTNET_EnableDiagnostics=0
COPY --from=publish --chown=$APP_UID:$APP_UID /out/ ./
COPY --from=publish --chown=$APP_UID:$APP_UID /connector-data/ /var/lib/pitcrew-connector/
USER $APP_UID
ENTRYPOINT ["./PitCrew.Connector.App"]
