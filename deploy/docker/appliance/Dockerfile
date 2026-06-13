# Image applicative Liakont.Host (.NET 10) pour l'appliance Docker (OPS01a, F12 §6.2).
# Build multi-stage : SDK pour compiler/publier, image runtime aspnet pour exécuter.
# Le CONTEXTE de build est la RACINE du dépôt (voir docker-compose.yml : build.context),
# pour accéder à global.json + Directory.*.props + tout src/ (graphe de projets du Host).

# Le SDK de l'image de build DOIT satisfaire global.json (épinglé 10.0.200, rollForward latestFeature) :
# le tag flottant 10.0 pointe sur le dernier SDK 10.0.x (≥ 10.0.200) et convient. ATTENTION à un cache
# local PÉRIMÉ de sdk:10.0 antérieur à 10.0.200 → `dotnet restore` échouerait (« compatible SDK not
# found »). Construire avec --pull (docker build --pull / docker compose build --pull) pour repartir
# d'une base à jour. Voir README §2/§5.
ARG DOTNET_SDK_TAG=10.0
ARG DOTNET_RUNTIME_TAG=10.0

# ─────────────────────────────────────────────────────────────────────────────
# Stage build
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG} AS build
WORKDIR /src

# Fichiers de build partagés d'abord (gestion centralisée des packages + SDK épinglé + sévérités
# d'analyseurs .editorconfig) : le restore ne se rejoue que si le graphe de dépendances change.
# Le .editorconfig est REQUIS au build : il porte les sévérités StyleCop du dépôt (SA1633/SA0001/…) ;
# sans lui, les analyseurs tournent en défaut et le build échoue (TreatWarningsAsErrors).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ ./src/

RUN dotnet restore src/Host/Liakont.Host/Liakont.Host.csproj
# UseAppHost=false : on lance via « dotnet Liakont.Host.dll », pas besoin de l'apphost natif.
RUN dotnet publish src/Host/Liakont.Host/Liakont.Host.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ─────────────────────────────────────────────────────────────────────────────
# Stage runtime
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG} AS runtime
WORKDIR /app

# Écoute HTTP interne sur 8080 (TLS terminé par Caddy en façade). Environnement Production.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish ./

# Répertoires de données persistés en volume (App_Data). Créés AVEC les droits de l'utilisateur
# applicatif non-root ($APP_UID, défini par l'image .NET) : un volume nommé vide hérite de ces
# droits à sa création → le service peut y écrire (trousseau DP, staging, PDF, coffre WORM).
RUN mkdir -p /app/App_Data/dataprotection-keys \
             /app/App_Data/staging-store \
             /app/App_Data/ingestion-pdf \
             /app/App_Data/archive-store \
    && chown -R $APP_UID:$APP_UID /app/App_Data

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Liakont.Host.dll"]
