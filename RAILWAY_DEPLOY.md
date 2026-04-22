# Деплой API на Railway (актуально под этот репозиторий)

Репозиторий уже содержит **`Dockerfile`** в **корне** и **`railway.json`** (сборка через Docker). Подключение аккаунта Railway и репозитория GitHub вы делаете вручную один раз; дальше деплой идёт сам при `git push`.

## Что вы деплоите

- Один сервис **API** (`ApocalypseLauncher.API`, .NET 8).
- Рекомендуется **PostgreSQL** от Railway (переменная `DATABASE_URL`).

## Шаг 1. Railway + GitHub

1. Зайдите на [railway.app](https://railway.app), войдите через GitHub.
2. **New Project** → **Deploy from GitHub repo** → выберите этот репозиторий.
3. У сервиса с API откройте **Settings**:
   - **Root Directory** оставьте **пустым** (корень репозитория), чтобы использовался корневой `Dockerfile`.
   - Убедитесь, что в корне виден `railway.json` с `"builder": "DOCKERFILE"`.

Не указывайте Root Directory `src/ApocalypseLauncher.API` — это старый сценарий без Docker и только запутает.

## Шаг 2. PostgreSQL

1. В проекте Railway: **New** → **Database** → **PostgreSQL**.
2. В сервисе **API** → **Variables** → **Add Reference** (или аналог) и подключите **`DATABASE_URL`** из базы к сервису API.

API сам читает `DATABASE_URL` из окружения (см. `Program.cs`).

## Шаг 3. Переменные окружения (обязательно)

В сервисе **API** → **Variables**:

| Переменная | Зачем |
|------------|--------|
| **`Jwt__SecretKey`** | Секрет подписи JWT и handoff сайт→лаунчер. Длинная случайная строка (лучше 48+ байт в base64). На Windows: `pwsh scripts/New-JwtSecret.ps1` |
| **`Cors__AllowedOrigins__0`** | Полный origin сайта, например `https://ваш-ник.github.io`. Без слэша в конце. |
| **`DATABASE_URL`** | Из Postgres (reference). |

В **Production** без **`Jwt__SecretKey`** (или с плейсхолдером из примера) приложение **не стартует**.

Если **`Cors__AllowedOrigins__0`** не задан, API **всё равно поднимается** и использует **`AllowAnyOrigin`** (лаунчер и сайт не ломаются). В логах будет предупреждение — для безопасности лучше указать HTTPS-оригин сайта:

```text
Cors__AllowedOrigins__0=https://ваш-статический-сайт.github.io
```

Опционально явный обход: `CORS_ALLOW_ANY_ORIGIN=true` / `Cors__AllowAnyOrigin=true`.

Шаблон без секретов: `scripts/railway-variables.example.env`.

## Шаг 4. Публичный URL

1. Сервис API → **Settings** → **Networking** → **Generate Domain**.
2. Проверка: откройте в браузере  
   `https://ВАШ-ДОМЕН.up.railway.app/api/health`  
   должен вернуться JSON со `status: "healthy"`.

## Шаг 5. Лаунчер и сайт

- **Лаунчер**: в `src/ApocalypseLauncher/Core/Services/ApiService.cs` базовый URL API должен совпадать с Railway (или настраивается в вашем UI лаунчера — смотрите проект).
- **Сайт** (`отдельный` репозиторий/хостинг): в HTML выставьте тот же URL в `data-auth-api` и в CSP `connect-src`, и добавьте этот же URL как **`Cors__AllowedOrigins__0`**.

## Шаг 6. Локальная проверка Docker (по желанию)

Из корня репозитория:

```powershell
pwsh scripts/build-api-docker.ps1
docker run --rm -e PORT=8080 -e Jwt__SecretKey="ВАША_СЛУЧАЙНАЯ_СТРОКА_НЕ_ИЗ_ПРИМЕРА" -p 8080:8080 srp-rp-api:local
```

Откройте `http://localhost:8080/api/health`.

## CI в GitHub

Workflow **`.github/workflows/ci-api.yml`**: при push в `main`/`master` собирается проект API и проверяется `docker build`. Ничего дополнительно включать не нужно.

## Частые ошибки

| Симптом | Что сделать |
|---------|-------------|
| Контейнер падает при старте | Логи Deployments → Logs; проверьте `Jwt__SecretKey` и CORS (см. выше). |
| С сайта «CORS» в консоли браузера | Точное совпадение `https://` + домен в `Cors__AllowedOrigins__0` с origin страницы. |
| 502 | API не слушает порт: в образе используется `docker-entrypoint.sh` и переменная **`PORT`** от Railway — не переопределяйте `ENTRYPOINT` вручную. |
| Certificate pinning в лаунчере | Отдельная настройка в `CertificatePinning.cs` (опционально). |

После первого успешного деплоя достаточно пушить в `main` — Railway пересоберёт образ из `Dockerfile`.
