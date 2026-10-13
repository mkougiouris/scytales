# syntax=docker/dockerfile:1

# One Dockerfile for both hosts: they share every project but the host itself, so they share the restore and
# build layers too. Pick the image with the target: `api` or `worker`. Build context: the repository root.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Project files first, so the restore layer is only rebuilt when a dependency changes.
COPY global.json ./
COPY App/Scytales.Domain/Scytales.Domain.csproj                 App/Scytales.Domain/
COPY App/Scytales.Application/Scytales.Application.csproj       App/Scytales.Application/
COPY App/Scytales.Infrastructure/Scytales.Infrastructure.csproj App/Scytales.Infrastructure/
COPY App/Hosts/Scytales.Hosting/Scytales.Hosting.csproj         App/Hosts/Scytales.Hosting/
COPY App/Hosts/Scytales.Api/Scytales.Api.csproj                 App/Hosts/Scytales.Api/
COPY App/Hosts/Scytales.Worker/Scytales.Worker.csproj           App/Hosts/Scytales.Worker/
RUN dotnet restore App/Hosts/Scytales.Api/Scytales.Api.csproj \
 && dotnet restore App/Hosts/Scytales.Worker/Scytales.Worker.csproj

COPY App/ App/

FROM build AS publish-api
RUN dotnet publish App/Hosts/Scytales.Api/Scytales.Api.csproj -c Release -o /out --no-restore -p:UseAppHost=false

FROM build AS publish-worker
RUN dotnet publish App/Hosts/Scytales.Worker/Scytales.Worker.csproj -c Release -o /out --no-restore -p:UseAppHost=false

# Debian/Ubuntu-based runtime: it ships tzdata, which the business time zone (WorkflowRules:TimeZoneId) needs.
# curl is added for the container health check; the runtime image has no HTTP client of its own.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
USER $APP_UID
EXPOSE 8080
# Both hosts expose the same health endpoints (Scytales.Hosting). Readiness, so that dependants wait for
# the database and the broker, not just for the process.
HEALTHCHECK --interval=5s --timeout=3s --start-period=20s --retries=5 \
  CMD curl --fail --silent --output /dev/null http://localhost:8080/health/ready || exit 1

FROM runtime AS api
COPY --from=publish-api /out .
ENTRYPOINT ["dotnet", "Scytales.Api.dll"]

FROM runtime AS worker
COPY --from=publish-worker /out .
ENTRYPOINT ["dotnet", "Scytales.Worker.dll"]
