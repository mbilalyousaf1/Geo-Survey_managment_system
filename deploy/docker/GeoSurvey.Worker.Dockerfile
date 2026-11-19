FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY src/Backend/ src/Backend/
RUN dotnet publish src/Backend/GeoSurvey.Provisioning.Worker/GeoSurvey.Provisioning.Worker.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "GeoSurvey.Provisioning.Worker.dll"]
