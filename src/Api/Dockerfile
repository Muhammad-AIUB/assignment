# syntax=docker/dockerfile:1
#
# School Admission API image.
# Build context is the REPOSITORY ROOT (not src/Api), because Api references Domain and Infrastructure:
#   docker build -f src/Api/Dockerfile -t school-admission-api .

# ---- build stage: full SDK, never shipped ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore using only the project files, so this layer stays cached until a package reference changes.
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        /p:UseAppHost=false

# ---- runtime stage: ASP.NET runtime only (Alpine, no SDK, no compilers) --------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

COPY --from=build /app/publish .

# Kestrel listens on 8080 (a non-root user cannot bind ports below 1024).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The base image ships an unprivileged "app" user (uid 1654). The app writes nothing to disk, so the
# root-owned, read-only-to-app publish output is fine.
USER app

# /health runs SELECT 1 against Postgres: 200 when the database answers, 503 when it does not.
# start-period covers the migrations that run at startup.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
    CMD wget -qO /dev/null http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "Api.dll"]
