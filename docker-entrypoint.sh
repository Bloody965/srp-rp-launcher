#!/bin/sh
set -e
# Railway (и др.) задают PORT в рантайме; без этого ASPNETCORE_URLS с $PORT в Dockerfile часто пустой.
PORT="${PORT:-8080}"
export ASPNETCORE_URLS="http://0.0.0.0:${PORT}"
exec dotnet /app/ApocalypseLauncher.API.dll
