# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Paths are relative to the build context root (repo root)
COPY DocumentIntelligence.sln ./
COPY src ./src
COPY tests ./tests

RUN dotnet restore src/DocumentService.Api/DocumentService.Api.csproj
RUN dotnet publish src/DocumentService.Api/DocumentService.Api.csproj -c Release -o /out

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# The aspnet image ships no HTTP client, so a container healthcheck has nothing to call
# the readiness endpoint with. Kubernetes and Container Apps probe over HTTP themselves and
# would not need this; compose does.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocumentService.Api.dll"]
