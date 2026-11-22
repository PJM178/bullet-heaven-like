# Backend image. Build from the REPO ROOT (the context must contain backend/
# and shared/), e.g. via deploy/docker-compose.yml or:
#   docker build -f deploy/Dockerfile -t bulletheavenlike-backend .
#
# Multi-stage: the big SDK image compiles, the small runtime image runs.

# --- build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project files first and restore, so Docker caches the NuGet
# restore layer and only redoes it when a .csproj changes.
COPY shared/BulletHeavenLike.Shared/BulletHeavenLike.Shared.csproj shared/BulletHeavenLike.Shared/
COPY backend/BulletHeavenLike.Backend/BulletHeavenLike.Backend.csproj backend/BulletHeavenLike.Backend/
RUN dotnet restore backend/BulletHeavenLike.Backend/BulletHeavenLike.Backend.csproj

COPY shared/ shared/
COPY backend/ backend/
RUN dotnet publish backend/BulletHeavenLike.Backend/BulletHeavenLike.Backend.csproj \
    -c Release -o /app --no-restore

# --- runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# The aspnet image already listens on 8080 (ASPNETCORE_HTTP_PORTS=8080)
# and runs as the non-root "app" user.
EXPOSE 8080
ENTRYPOINT ["dotnet", "BulletHeavenLike.Backend.dll"]
