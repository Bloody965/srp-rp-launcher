# Сборка образа API локально (нужны Docker Desktop и .NET не обязательны — всё внутри Dockerfile).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

docker build -t srp-rp-api:local .
Write-Host "OK. Запуск: docker run --rm -e PORT=8080 -e Jwt__SecretKey=DEV_ONLY_CHANGE_ME_MIN_32_CHARS_LONG_SECRET -p 8080:8080 srp-rp-api:local" -ForegroundColor Green
