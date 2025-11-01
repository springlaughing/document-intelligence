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
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocumentService.Api.dll"]
