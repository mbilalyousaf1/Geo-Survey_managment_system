FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props .editorconfig ./
COPY src/Backend/ src/Backend/
RUN dotnet restore src/Backend/GeoSurvey.Api/GeoSurvey.Api.csproj

RUN dotnet publish src/Backend/GeoSurvey.Api/GeoSurvey.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "GeoSurvey.Api.dll"]
