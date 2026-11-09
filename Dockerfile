# One image: ASP.NET Core API + React build served from wwwroot.
# Build: docker build --build-arg VITE_FIREBASE_API_KEY=... (4 VITE_FIREBASE_* args) -t quests .

# --- Web: React build (Firebase web config is public and baked into the bundle) ---
FROM node:24-alpine AS web
ARG VITE_FIREBASE_API_KEY
ARG VITE_FIREBASE_AUTH_DOMAIN
ARG VITE_FIREBASE_PROJECT_ID
ARG VITE_FIREBASE_APP_ID
ENV VITE_FIREBASE_API_KEY=$VITE_FIREBASE_API_KEY \
    VITE_FIREBASE_AUTH_DOMAIN=$VITE_FIREBASE_AUTH_DOMAIN \
    VITE_FIREBASE_PROJECT_ID=$VITE_FIREBASE_PROJECT_ID \
    VITE_FIREBASE_APP_ID=$VITE_FIREBASE_APP_ID
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# --- API: publish (restore first for layer caching) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/backend
COPY backend/nuget.config ./
COPY backend/Quests.Api/Quests.Api.csproj Quests.Api/
RUN dotnet restore Quests.Api/Quests.Api.csproj
COPY backend/Quests.Api/ Quests.Api/
RUN dotnet publish Quests.Api/Quests.Api.csproj -c Release -o /out --no-restore

# --- Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /src/frontend/dist ./wwwroot
RUN mkdir -p /app/data /app/logs && chown "$APP_UID" /app/data /app/logs
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/app/data/quests.db" \
    LOG_FILE=/app/logs/quests.log
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quests.Api.dll"]
