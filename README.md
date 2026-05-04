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

**Один URL API:** в лаунчере класс **`SrpProjectEndpoints`** (`src/ApocalypseLauncher/Core/SrpProjectEndpoints.cs`) — дефолт совпадает с сайтом. Переопределение на машине игрока: переменная окружения **`SRP_API_BASE_URL`**. На Railway для строгого CORS к сайту: **`Cors__AllowedOrigins__0`** или **`SITE_PUBLIC_ORIGIN`**.

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
export Jwt__SecretKey="ваш_секретный_ключ"
export ConnectionStrings__DATABASE_URL="postgresql://..."
# или полная строка Npgsql (удобно для Amvera и др.):
# export ConnectionStrings__DefaultConnection="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Prefer;Trust Server Certificate=true"
export MinecraftServer__Address="ваш_ip"
export MinecraftServer__Port="25565"
```

## 🌐 Деплой на production

### Amvera.ru и управляемый PostgreSQL

Если в панели Amvera задан только **SQLite** из `appsettings` (`Data Source=...`), база живёт в контейнере и при пересборке/смене тома **данные могут пропадать** — тогда «ломаются» аккаунты, UUID и скины в Yggdrasil.

1. Создайте в Amvera кластер **PostgreSQL** (тариф не ниже «Начальный», как рекомендует Amvera).
2. В **проекте API** добавьте переменную окружения **`ConnectionStrings__DefaultConnection`** со строкой подключения **Npgsql**, например:

`Host=amvera-<username>-cnpg-<project_name>-rw;Port=5432;Database=<имя_бд>;Username=<пользователь>;Password=<пароль>;SSL Mode=Prefer;Trust Server Certificate=true`

Хост **`-rw`** (чтение/запись) и имя БД смотрите в разделе «Инфо» у кластера PostgreSQL в панели Amvera. Имя `postgres` и пользователь `postgres` зарезервированы — используйте свои имя БД и пользователя, которые вы задали при создании кластера.

3. Альтернатива: **`DATABASE_URL`** или **`ConnectionStrings__DATABASE_URL`** в виде `postgresql://user:pass@host:5432/dbname` — тоже поддерживается.

После первого старта с PostgreSQL схема создаётся через `EnsureCreated()`; при обновлении API применяются точечные SQL-правки только для PostgreSQL.

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
