# Railway Deployment Guide

## Быстрый деплой на Railway.app

### Шаг 1: Создание аккаунта

1. Перейдите на https://railway.app
2. Нажмите "Start a New Project"
3. Войдите через GitHub

### Шаг 2: Подключение репозитория

1. Выберите "Deploy from GitHub repo"
2. Найдите репозиторий `Bloody965/srp-rp-launcher`
3. Нажмите "Deploy Now"

### Шаг 3: Настройка проекта

Railway автоматически определит .NET проект, но нужно указать путь:

1. В настройках проекта добавьте:
   - **Root Directory**: `src/ApocalypseLauncher.API`
   - **Build Command**: `dotnet publish -c Release -o out`
   - **Start Command**: `dotnet out/ApocalypseLauncher.API.dll`

### Шаг 4: Добавление PostgreSQL

1. Нажмите "New" → "Database" → "Add PostgreSQL"
2. Railway автоматически создаст переменную `DATABASE_URL`
3. API автоматически подключится к PostgreSQL

### Шаг 5: Настройка переменных окружения

Добавьте в Variables:

```bash
# JWT Secret (ОБЯЗАТЕЛЬНО!)
Jwt__SecretKey=СГЕНЕРИРУЙТЕ_СЛУЧАЙНУЮ_СТРОКУ_64_СИМВОЛА

# Minecraft Server (ваш IP)
MinecraftServer__Address=185.9.145.97
MinecraftServer__Port=30002

# CORS (опционально)
Cors__AllowedOrigins__0=https://your-domain.com
```

**Генерация JWT ключа:**
```bash
openssl rand -base64 64
```

### Шаг 6: Получение URL

После деплоя Railway даст вам URL типа:
```
https://srp-rp-launcher-production.up.railway.app
```

Это и есть ваш API URL!

### Шаг 7: Настройка HTTPS сертификата

1. Railway автоматически выдает SSL сертификат
2. Получите хеш сертификата:

```bash
openssl s_client -connect srp-rp-launcher-production.up.railway.app:443 </dev/null 2>/dev/null | openssl x509 -pubkey -noout | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | openssl enc -base64
```

3. Добавьте хеш в `src/ApocalypseLauncher/Core/Security/CertificatePinning.cs`:

```csharp
private static readonly HashSet<string> TrustedCertificateHashes = new()
{
    "ВАШ_ХЕШ_СЕРТИФИКАТА_ЗДЕСЬ"
};
```

### Шаг 8: Обновление лаунчера

В `src/ApocalypseLauncher/Core/Services/ApiService.cs`:

```csharp
public ApiService(string baseUrl = "https://srp-rp-launcher-production.up.railway.app")
```

Пересоберите лаунчер:
```bash
cd src/ApocalypseLauncher
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Проверка работы

1. Откройте в браузере:
   ```
   https://your-railway-url.up.railway.app/api/health
   ```

   Должно вернуть:
   ```json
   {
     "status": "healthy",
     "timestamp": "2026-04-18T...",
     "version": "1.0.0"
   }
   ```

2. Запустите лаунчер и попробуйте зарегистрироваться

## Мониторинг

Railway предоставляет:
- Логи в реальном времени
- Метрики CPU/RAM
- Автоматические рестарты при падении
- Автодеплой при push в GitHub

## Стоимость

- **Free tier**: 500 часов/месяц (~20 дней)
- **Hobby**: $5/месяц - unlimited
- **Pro**: $20/месяц - больше ресурсов

Для начала Free tier достаточно!

## Troubleshooting

### Ошибка "JWT SecretKey not configured"
Добавьте переменную `Jwt__SecretKey` в Railway Variables

### Ошибка подключения к БД
Railway автоматически создает `DATABASE_URL`, проверьте что она есть

### 502 Bad Gateway
Проверьте логи в Railway Dashboard, возможно ошибка при старте

### Certificate pinning fails
Убедитесь что добавили правильный хеш сертификата Railway

## Следующие шаги

1. Настройте custom domain (опционально)
2. Добавьте мониторинг (Sentry, LogRocket)
3. Настройте бэкапы PostgreSQL
4. Добавьте CDN для модпаков (Cloudflare R2, AWS S3)

---

**Готово!** Ваш API работает 24/7 с автоматическими обновлениями из GitHub.
