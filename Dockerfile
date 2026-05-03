FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/ApocalypseLauncher.API/ApocalypseLauncher.API.csproj", "src/ApocalypseLauncher.API/"]
RUN dotnet restore "src/ApocalypseLauncher.API/ApocalypseLauncher.API.csproj"
COPY src/ApocalypseLauncher.API/ src/ApocalypseLauncher.API/
WORKDIR "/src/src/ApocalypseLauncher.API"
RUN dotnet publish "ApocalypseLauncher.API.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Создаём папки для данных
RUN mkdir -p /app/data /app/modpacks

COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["/app/docker-entrypoint.sh"]
