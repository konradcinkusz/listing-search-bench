# Build context is the repository root: `docker build -f src/ListingSearch.SearchService/Dockerfile .`
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ListingSearch.ServiceDefaults/ListingSearch.ServiceDefaults.csproj src/ListingSearch.ServiceDefaults/
COPY src/ListingSearch.SearchService/ListingSearch.SearchService.csproj src/ListingSearch.SearchService/
RUN dotnet restore src/ListingSearch.SearchService/ListingSearch.SearchService.csproj

COPY src/ListingSearch.ServiceDefaults/ src/ListingSearch.ServiceDefaults/
COPY src/ListingSearch.SearchService/ src/ListingSearch.SearchService/
COPY evals/fixtures/ evals/fixtures/

RUN dotnet publish src/ListingSearch.SearchService/ListingSearch.SearchService.csproj \
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
ENTRYPOINT ["dotnet", "ListingSearch.SearchService.dll"]
