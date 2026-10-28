FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CargoHub.Api/CargoHub.Api.csproj CargoHub.Api/
RUN dotnet restore CargoHub.Api/CargoHub.Api.csproj
COPY src/CargoHub.Api/ CargoHub.Api/
RUN dotnet publish CargoHub.Api/CargoHub.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CargoHub.Api.dll"]
