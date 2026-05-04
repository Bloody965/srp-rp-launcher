# Apocalypse Launcher

Защищенный лаунчер для Minecraft с системой аутентификации и автоматическим обновлением модпаков.

## 🎮 Возможности

- **Безопасная аутентификация** - JWT токены, BCrypt хеширование, rate limiting
- **Автообновление модпаков** - SHA256 проверка целостности файлов
- **Система скинов и плащей** - кастомные скины для игроков
- **Статус сервера** - отображение онлайн игроков
- **Recovery code система** - восстановление доступа без email
- **Аудит действий** - полное логирование всех операций

## 🔒 Безопасность

### Реализованная защита:
- ✅ BCrypt хеширование паролей (12 раундов)
- ✅ JWT токены с валидацией и истечением
- ✅ Rate limiting против брутфорса
- ✅ SQL injection защита
- ✅ Валидация всех входных данных
- ✅ Система сессий с отслеживанием IP
- ✅ Аудит логи всех действий

### Rate Limits:
- Регистрация: 3 попытки/час с IP
- Вход: 5 попыток/15 минут с IP
- Сброс пароля: 5 попыток/15 минут с IP

## 🚀 Быстрый старт

### Деплой API на Railway

В корне репозитория: **`Dockerfile`**, **`railway.json`**, **`docker-entrypoint.sh`** (слушает порт из переменной **`PORT`**). Пошаговая инструкция: **[RAILWAY_DEPLOY.md](RAILWAY_DEPLOY.md)**. Скрипты: **`scripts/build-api-docker.ps1`**, **`scripts/New-JwtSecret.ps1`**, шаблон переменных **`scripts/railway-variables.example.env`**.

**Один URL API:** в лаунчере **`SrpProjectEndpoints`** (`src/ApocalypseLauncher/Core/SrpProjectEndpoints.cs`) — встроенный дефолт указывает на прод API; переопределение без пересборки: **`SRP_API_BASE_URL`** или файл **`%AppData%\\SRP-RP-Launcher\\api-base.url`** (одна строка, например `https://ваш-api.amvera.io`). Превью скина в лаунчере подтягивает `/api/skins/...` с текущего хоста, даже если API когда-то вернул URL со старым доменом.

### Требования
- .NET 8.0 SDK
- Windows 10/11 (для лаунчера)
- Linux/Windows (для API сервера)

### 1. Настройка API сервера

```bash
cd src/ApocalypseLauncher.API

# Скопируйте пример конфигурации
cp appsettings.example.json appsettings.json

# Отредактируйте appsettings.json:
# - Замените JWT SecretKey на случайную строку (64+ символов)
# - Укажите IP и порт вашего Minecraft сервера
```

**Генерация безопасного JWT ключа:**
```bash
dotnet run --generate-jwt-key
```

### 2. Запуск API сервера

```bash
cd src/ApocalypseLauncher.API
dotnet run
```

API будет доступен на `http://localhost:5000`

### 3. Сборка лаунчера

```bash
cd src/ApocalypseLauncher
dotnet publish -c Release -r win-x64 --self-contained
```

Готовый лаунчер будет в `bin/Release/net8.0/win-x64/publish/`

## 📁 Структура проекта

```
├── src/
│   ├── ApocalypseLauncher/          # Клиент лаунчера (Avalonia UI)
│   └── ApocalypseLauncher.API/      # API сервер (ASP.NET Core)
├── installer/                        # Установщик (WinForms)
└── README.md
```

## 🔧 Конфигурация

### appsettings.json (API сервер)

```json
{
  "Jwt": {
    "SecretKey": "ВАШ_СЛУЧАЙНЫЙ_КЛЮЧ_64_СИМВОЛА_МИНИМУМ",
    "ExpirationHours": "24"
  },
  "MinecraftServer": {
    "Address": "ВАШ_IP",
    "Port": "25565"
  }
}
```

### Переменные окружения (для production)

```bash
export JWT_SECRET_KEY="ваш_секретный_ключ"
# или export Jwt__SecretKey="..." если панель разрешает "__"
export POSTGRES_CONNECTION_STRING="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Prefer;Trust Server Certificate=true"
# или export DATABASE_URL="postgresql://..."
export MinecraftServer__Address="ваш_ip"
export MinecraftServer__Port="25565"
```

## 🌐 Деплой на production

### Amvera.ru и управляемый PostgreSQL

Если в панели Amvera задан только **SQLite** из `appsettings` (`Data Source=...`), база живёт в контейнере и при пересборке/смене тома **данные могут пропадать** — тогда «ломаются» аккаунты, UUID и скины в Yggdrasil.

1. Создайте в Amvera кластер **PostgreSQL** (тариф не ниже «Начальный», как рекомендует Amvera).
2. В **проекте API** задайте строку PostgreSQL. В Amvera **имя переменной** часто должно быть только из латиницы, цифр и `_` (без двойного `__`). Используйте одно из имён:
   - **`POSTGRES_CONNECTION_STRING`** — полная строка **Npgsql** (рекомендуется для Amvera), например:  
     `Host=amvera-<username>-cnpg-<project_name>-rw;Port=5432;Database=<имя_бд>;Username=<пользователь>;Password=<пароль>;SSL Mode=Prefer;Trust Server Certificate=true`
   - либо **`DATABASE_URL`** в виде `postgresql://user:pass@host:5432/dbname`
   - либо **`ConnectionStrings__DefaultConnection`** — только если панель **разрешает** двойное подчёркивание в имени

Хост **`-rw`** (чтение/запись) и имя БД смотрите в разделе «Инфо» у кластера PostgreSQL в панели Amvera. Имя `postgres` и пользователь `postgres` зарезервированы — используйте свои имя БД и пользователя, которые вы задали при создании кластера.

3. Альтернатива: **`DATABASE_URL`** или **`ConnectionStrings__DATABASE_URL`** в виде `postgresql://user:pass@host:5432/dbname` — тоже поддерживается.

После первого старта с PostgreSQL схема создаётся через `EnsureCreated()`; при обновлении API применяются точечные SQL-правки только для PostgreSQL.

### Скины в одиночке / Yggdrasil

Текстура в профиле отдаётся ссылкой вида `{BaseUrl}/api/skins/download/...`. **authlib-injector** разрешает только хосты из `skinDomains` в метаданных Yggdrasil — они берутся из того же **публичного базового URL**, что и `BaseUrl` на API.

- В Amvera (и где нельзя `Jwt__SecretKey` с `__`) задайте **`BASE_URL`** — полный публичный адрес API с `https://`, **без** завершающего слэша, **ровно тот же хост**, по которому ходит лаунчер (в клиенте это `SRP_API_BASE_URL` или значение по умолчанию из сборки).
- Если `BASE_URL` не задан, сервер может подставить **fallback-домен из кода** (Railway) — тогда ссылки на скин и домен в `skinDomains` **не совпадут** с реальным API, текстуры отбрасываются, в игре виден **чужой или дефолтный** скин (в т.ч. в одиночке).
- Убедитесь, что в лаунчере **загружен скин** (в БД есть активная запись `PlayerSkins`); иначе в профиле пустые текстуры.

Моды вроде **CustomSkinLoader** могут переопределять источник скина — при отладке временно отключите их в сборке.

### Railway.app / Render.com

1. Форкните репозиторий
2. Подключите к Railway/Render
3. Настройте переменные окружения:
   - `Jwt__SecretKey`
   - `ConnectionStrings__DATABASE_URL` или `DATABASE_URL` (PostgreSQL URI), либо `ConnectionStrings__DefaultConnection` (строка Npgsql с `Host=`)
   - `MinecraftServer__Address`
   - `MinecraftServer__Port`

### Docker

```bash
docker build -t apocalypse-launcher-api .
docker run -p 5000:5000 \
  -e Jwt__SecretKey="ваш_ключ" \
  -e MinecraftServer__Address="ваш_ip" \
  apocalypse-launcher-api
```

## 📚 API Документация

### Аутентификация

**POST /api/auth/register**
```json
{
  "username": "Player123",
  "password": "SecurePass123"
}
```

**POST /api/auth/login**
```json
{
  "username": "Player123",
  "password": "SecurePass123"
}
```

**POST /api/auth/reset-password**
```json
{
  "username": "Player123",
  "recoveryCode": "ABCD1234EFGH5678",
  "newPassword": "NewPass123"
}
```

### Модпаки (требуется токен)

**GET /api/modpack/version**
- Получить информацию о последней версии

**GET /api/modpack/download**
- Скачать модпак (только с валидным JWT)

### Скины (требуется токен)

**POST /api/skins/upload**
- Загрузить скин (PNG, 64x64 или 64x32)

**GET /api/skins/{username}**
- Получить скин игрока

## 🛡️ Security Checklist для production

- [ ] Замените JWT SecretKey на криптографически стойкий ключ
- [ ] Настройте HTTPS с валидным сертификатом
- [ ] Ограничьте CORS для конкретных доменов
- [ ] Включите whitelist систему (`Modpack:RequireWhitelist: true`)
- [ ] Настройте резервное копирование базы данных
- [ ] Настройте мониторинг и алерты
- [ ] Обновите все NuGet пакеты до последних версий
- [ ] Используйте переменные окружения вместо appsettings.json
- [ ] Настройте firewall правила
- [ ] Включите логирование в внешнюю систему (Sentry, etc)

## ⚠️ Важные замечания

1. **Никогда не коммитьте** `appsettings.json` с реальными секретами
2. **Используйте переменные окружения** для production
3. **Регулярно обновляйте** зависимости
4. **Мониторьте логи** на подозрительную активность
5. **Делайте бэкапы** базы данных

## 📝 Лицензия

MIT License - см. [LICENSE](LICENSE)

## 🤝 Вклад в проект

Pull requests приветствуются! Для крупных изменений сначала откройте issue.

## 📧 Поддержка

Если нашли уязвимость безопасности - создайте приватный security advisory в GitHub.

---

**Создано для SRP-RP проекта**
