FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json NeuerKids.slnx ./
COPY src/NeuerKids/NeuerKids.csproj src/NeuerKids/packages.lock.json src/NeuerKids/
RUN dotnet restore src/NeuerKids/NeuerKids.csproj --locked-mode
COPY src/NeuerKids/ src/NeuerKids/
RUN dotnet publish src/NeuerKids/NeuerKids.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
RUN mkdir -p /app/App_Data && chown app:app /app/App_Data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "NeuerKids.dll"]
