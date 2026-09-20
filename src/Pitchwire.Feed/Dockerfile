# Built from the repository root: the layer projects and the shared configuration all have to be in
# the context.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# .editorconfig carries the analyzer severities. Without it the build inside the image runs with
# different rules from the one on a developer machine, and a build that passed locally fails here.
COPY global.json dotnet.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/

RUN dotnet publish src/Pitchwire.Feed/Pitchwire.Feed.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Pitchwire.Feed.dll"]
