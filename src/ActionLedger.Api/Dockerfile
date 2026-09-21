# syntax=docker/dockerfile:1

# The api image, built from the repository root:
#
#   docker build -f src/ActionLedger.Api/Dockerfile .
#
# AD-17 — the api never migrates at startup. Schema ships only through the EF migration bundle,
# and this is where the bundle is produced: `migrate` and the CD step both run the executable
# this build copies out of the final image.

# --- build ---------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from just the files that decide what gets restored, so a source-only change does
# not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .config/dotnet-tools.json .config/
COPY src/ActionLedger.Domain/*.csproj src/ActionLedger.Domain/
COPY src/ActionLedger.Application/*.csproj src/ActionLedger.Application/
COPY src/ActionLedger.Infrastructure/*.csproj src/ActionLedger.Infrastructure/
COPY src/ActionLedger.Api/*.csproj src/ActionLedger.Api/

RUN dotnet restore src/ActionLedger.Api/ActionLedger.Api.csproj

COPY src/ src/

RUN dotnet build src/ActionLedger.Api/ActionLedger.Api.csproj -c Release --no-restore

# --- publish -------------------------------------------------------------------------------------
FROM build AS publish
RUN dotnet publish src/ActionLedger.Api/ActionLedger.Api.csproj -c Release --no-build -o /app/publish

# --- migration bundle ----------------------------------------------------------------------------
# `dotnet ef` builds the model, which means constructing AppDbContext, which means a connection
# string has to be present — the bundle never opens it. The value below is a design-time
# placeholder and no more a secret than the shape of the connection string; the real one arrives
# as `--connection` when `migrate` runs the bundle.
FROM build AS bundle
ENV Database__ConnectionString="Host=localhost;Database=actionledger;Username=bundle;Password=bundle"

RUN dotnet tool restore \
 && dotnet dotnet-ef migrations bundle \
        --project src/ActionLedger.Infrastructure \
        --startup-project src/ActionLedger.Api \
        --configuration Release \
        --no-build \
        --force \
        --output /app/migrate/efbundle

# --- final ---------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql probes for GSSAPI on every connection. Without it the connection still succeeds, but
# the probe writes "Cannot load library libgssapi_krb5.so.2" to stderr — which, in front of a
# `docker compose up`, reads exactly like the migration failing. One small package, no noise.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=publish /app/publish ./
# The bundle rides along in the api image so `migrate` and `api` are one artefact at one SHA —
# a schema and the code that expects it can never be a version apart.
COPY --from=bundle /app/migrate/ /app/migrate/

# Runs as the non-root user the .NET base images define.
USER $APP_UID

ENTRYPOINT ["dotnet", "ActionLedger.Api.dll"]
