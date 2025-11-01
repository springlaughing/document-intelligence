# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY DocumentIntelligence.sln ./
COPY src ./src
COPY tests ./tests
RUN dotnet restore src/AnalysisService.Worker/AnalysisService.Worker.csproj
RUN dotnet publish src/AnalysisService.Worker/AnalysisService.Worker.csproj -c Release -o /out /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "AnalysisService.Worker.dll"]
