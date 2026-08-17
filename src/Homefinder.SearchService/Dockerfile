# Build context is the repository root: `docker build -f src/Homefinder.SearchService/Dockerfile .`
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Homefinder.ServiceDefaults/Homefinder.ServiceDefaults.csproj src/Homefinder.ServiceDefaults/
COPY src/Homefinder.SearchService/Homefinder.SearchService.csproj src/Homefinder.SearchService/
RUN dotnet restore src/Homefinder.SearchService/Homefinder.SearchService.csproj

COPY src/Homefinder.ServiceDefaults/ src/Homefinder.ServiceDefaults/
COPY src/Homefinder.SearchService/ src/Homefinder.SearchService/
COPY evals/fixtures/ evals/fixtures/

RUN dotnet publish src/Homefinder.SearchService/Homefinder.SearchService.csproj \
    --configuration Release \
    --output /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# ICU/tzdata required, not trimmed — InvariantGlobalization=false (Directory.Build.props).
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app .

USER $APP_UID
ENTRYPOINT ["dotnet", "Homefinder.SearchService.dll"]
