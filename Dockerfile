FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/ApocalypseLauncher.API/ApocalypseLauncher.API.csproj", "ApocalypseLauncher.API/"]
RUN dotnet restore "ApocalypseLauncher.API/ApocalypseLauncher.API.csproj"
COPY src/ApocalypseLauncher.API/ ApocalypseLauncher.API/
WORKDIR "/src/ApocalypseLauncher.API"
RUN dotnet build "ApocalypseLauncher.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ApocalypseLauncher.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Создаём папки для данных
RUN mkdir -p /app/data /app/modpacks

ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ApocalypseLauncher.API.dll"]
